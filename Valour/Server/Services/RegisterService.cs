using SendGrid;
using Valour.Server.Database;
using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Services;

public class RegisterService
{
    private const string ValourWelcome = "## Welcome to Valour!\nI'm *Victor*, the Valour mascot. I'm here to help you get started. " +
                                         "If you have any questions, feel free to ask me. I may not be fast to respond (I am run by humans!) " +
                                         "You can also ask other users, or check out the Valour Central planet for more information." +
                                         " Some basics: \n- Valour communities are named Planets! You can join or create planets for your interests. " +
                                         "\n- You can also add friends and direct message them. " +
                                         "\n- Valour (desktop) supports opening multiple windows with controls on the top right of each window. ";
    
    private readonly PlanetMemberService _memberService;
    private readonly DirectChatChannelService _dmService;
    private readonly UserFriendService _friendService;
    private readonly UserService _userService;

    private readonly ValourDB _db;
    
    private readonly ILogger<RegisterService> _logger;
    
    public RegisterService(
        PlanetMemberService memberService,
        DirectChatChannelService dmService,
        UserFriendService friendService,
        UserService userService,
        ILogger<RegisterService> logger,
        ValourDB db)
    {
        _memberService = memberService;
        _dmService = dmService;
        _friendService = friendService;
        _userService = userService;
        _logger = logger;
        _db = db;
    }
    
    public async Task<TaskResult> RegisterUserAsync(RegisterUserRequest request, HttpContext ctx)
    {
        if (await _db.Users.AnyAsync(x => x.Name.ToLower() == request.Username.ToLower()))
            return new(false, "Username is taken");
        
        var now = DateTime.Today;
        var age = now.Year - request.DateOfBirth.Year;
        if (request.DateOfBirth > now.AddYears(-age)) age--;

        if (age < 13)
            return new TaskResult(false, "You must be 13 to use Valour. Sorry!");
        
        if (await _db.UserEmails.AnyAsync(x => x.Email.ToLower() == request.Email))
            return new(false, "This email has already been used");

        var emailValid = UserUtils.TestEmail(request.Email);
        if (!emailValid.Success)
            return new(false, emailValid.Message);

        // Check for whole blocked emails
        if (await _db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == request.Email.ToLower()))
            return new(false, "Include request in body"); // Vague on purpose

        var host = request.Email.Split('@')[1];

        // Check for blocked host
        if (await _db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == host.ToLower()))
            return new(false, "Include request in body"); // Vague on purpose


        var usernameValid = UserUtils.TestUsername(request.Username);
        if (!usernameValid.Success)
            return new(false, usernameValid.Message);

        var passwordValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passwordValid.Success)
            return new(false, passwordValid.Message);

        Referral refer = null;
        if (request.Referrer != null && !string.IsNullOrWhiteSpace(request.Referrer))
        {
            request.Referrer = request.Referrer.Trim();
            var referUser = await _userService.GetByNameAsync(request.Referrer);
            if (referUser is null)
                return new(false, "Referrer not found");

            var month_refers = await _db.Referrals.CountAsync(x => x.Created > DateTime.UtcNow.AddDays(-30) && x.ReferrerId == referUser.Id);
            
            // Calculate referral reward
            // reward is halved every 5 referrals in the month
            // to prevent a streamer from wrecking the economy
            var reward = 50.0m / (1 + (month_refers / 5));
            
            refer = new Referral()
            {
                ReferrerId = referUser.Id,
                Created = DateTime.UtcNow,
                Reward = reward,
            };
        }

        byte[] salt = PasswordManager.GenerateSalt();
        byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

        await using var tran = await _db.Database.BeginTransactionAsync();

        User user = null;
        
        try
        {
            user = new()
            {
                Id = IdManager.Generate(),
                Name = request.Username,
                Tag = await GetUniqueTag(request.Username),
                TimeJoined = DateTime.UtcNow,
                TimeLastActive = DateTime.UtcNow,
                Compliance = true, // All new users should be compliant
            };

            _db.Users.Add(user.ToDatabase());
            await _db.SaveChangesAsync();

            if (refer != null)
            {
                refer.UserId = user.Id;
                await _db.Referrals.AddAsync(refer.ToDatabase());
                
                var referAccount = await _db.EcoAccounts.FirstOrDefaultAsync(x => x.UserId == refer.ReferrerId && x.CurrencyId == ISharedCurrency.ValourCreditsId && x.AccountType == AccountType.User);
                referAccount.BalanceValue += refer.Reward;

                _db.EcoAccounts.Update(referAccount);
            }

            UserPrivateInfo userPrivateInfo = new()
            {
                Email = request.Email,
                Verified = false,
                UserId = user.Id,
                BirthDate = request.DateOfBirth,
                Locality = request.Locality
            };

            _db.UserEmails.Add(userPrivateInfo.ToDatabase());

            Valour.Database.Credential cred = new()
            {
                Id = IdManager.Generate(),
                CredentialType = Valour.Database.CredentialType.PASSWORD,
                Identifier = request.Email,
                Salt = salt,
                Secret = hash,
                UserId = user.Id
            };

            _db.Credentials.Add(cred);

            Valour.Database.UserProfile profile = new()
            {
                Id = user.Id,
                Headline = "New to Valour!",
                Bio = "I'm new to Valour. Please show me around!",
                BorderColor = "#fff",
                AnimatedBorder = false,
            };

            _db.UserProfiles.Add(profile);
            
            Valour.Database.Economy.EcoAccount globalAccount = new()
            {
                Id = IdManager.Generate(),
                UserId = user.Id,
                CurrencyId = ISharedCurrency.ValourCreditsId,
                PlanetId = ISharedPlanet.ValourCentralId,
            };
        
            _db.EcoAccounts.Add(globalAccount);

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = user.Id
            };

            _db.EmailConfirmCodes.Add(confirmCode.ToDatabase());
            await _db.SaveChangesAsync();

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);

            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return new(false, "Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();
        
        // These onboarding steps are not critical so it's outside of the main transaction
        try
        {
            // Create Valour Onboarding membership
            var onboardingMember = await _memberService.AddMemberAsync(ISharedPlanet.ValourCentralId, user.Id);
            if (!onboardingMember.Success)
            {
                Console.WriteLine("-- Error adding onboarding member! --");
                Console.WriteLine(onboardingMember.Message);
            }
            
            // Add Victor friendship
            var f0 = await _friendService.AddFriendAsync(user.Id, ISharedUser.VictorUserId);
            var f1 = await _friendService.AddFriendAsync(ISharedUser.VictorUserId, user.Id);
            if (!f0.Success || !f1.Success)
            {
                Console.WriteLine("-- Error adding Victor friendship! --");
                Console.WriteLine(f0.Message);
                Console.WriteLine(f1.Message);
            }

            // Send direct message from Victor to user
            var victorDm = await _dmService.CreateAsync(user.Id, ISharedUser.VictorUserId);
            if (victorDm.Success)
            {
                var victorMessage = await _dmService.PostMessageAsync(victorDm.Data, new DirectMessage()
                {
                    Content = ValourWelcome,
                    TimeSent = DateTime.UtcNow,
                    ChannelId = victorDm.Data.Id,
                    AuthorUserId = ISharedUser.VictorUserId,
                    Fingerprint = Guid.NewGuid().ToString(),
                }, ISharedUser.VictorUserId);

                if (!victorMessage.Success)
                {
                    Console.WriteLine("-- Error sending Victor DM! --");
                    Console.WriteLine(victorMessage.Message);
                }
            }
            else
            {
                Console.WriteLine("-- Error adding Victor DM channel! --");
                Console.WriteLine(victorDm.Message);
            }

        }
        catch (Exception e)
        {
            Console.WriteLine("-- Error doing onboarding steps! --");
            Console.WriteLine(e.Message);
        }

        return new(true, "Success");
    }
    
    public async Task<string> GetUniqueTag(string username)
    {
        var existing = await _db.Users.Where(x => x.Name.ToLower() == username.ToLower()).Select(x => x.Tag).ToListAsync();

        string tag;
        
        do
        {
            tag = GenerateRandomTag();
        } while (existing.Contains(tag));

        return tag;
    }
    
    // TODO: Prevent the one in 1.6 million chance that you will get the tag F***, along with other 'bad words'
    // Just passed by this and realized the chances are far higher when accounting for similar-looking characters
    private string GenerateRandomTag()
    {
        return new string(Enumerable.Repeat(ISharedUser.TagChars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
    
    public async Task<TaskResult> ResendRegistrationEmail(UserPrivateInfo userPrivateInfo, HttpContext ctx, RegisterUserRequest request)
    {
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.EmailConfirmCodes.RemoveRange(_db.EmailConfirmCodes.Where(x => x.UserId == userPrivateInfo.UserId));

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = userPrivateInfo.UserId
            };

            _db.EmailConfirmCodes.Add(confirmCode.ToDatabase());
            await _db.SaveChangesAsync();

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);
            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return new(false, "Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    private static async Task<Response> SendRegistrationEmail(HttpRequest request, string email, string code)
    {
        var host = request.Host.ToUriComponent();
        string link = $"{request.Scheme}://{host}/api/users/verify/{code}";

        string emsg = $@"<body>
                                  <h2 style='font-family:Helvetica;'>
                                    Welcome to Valour!
                                  </h2>
                                  <p style='font-family:Helvetica;>
                                    To verify your new account, please use the following link: 
                                  </p>
                                  <p style='font-family:Helvetica;'>
                                    <a href='{link}'>Verify</a>
                                  </p>
                                </body>";

        string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\n{link}";

        var result = await EmailManager.SendEmailAsync(email, "Valour Registration", rawmsg, emsg);
        return result;
    }
}