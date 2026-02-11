using SendGrid;
using Valour.Config.Configs;
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
                                         " Some basics: \n- Valour communities are named HostedPlanets! You can join or create planets for your interests. " +
                                         "\n- You can also add friends and direct message them. " +
                                         "\n- Valour (desktop) supports opening multiple windows with controls on the top right of each window. ";
    
    private readonly PlanetMemberService _memberService;
    private readonly ChannelService _channelService;
    private readonly UserFriendService _friendService;
    private readonly UserService _userService;
    private readonly MessageService _messageService;

    private readonly ValourDb _db;
    
    private readonly ILogger<RegisterService> _logger;
    
    public RegisterService(
        PlanetMemberService memberService,
        ChannelService channelService,
        UserFriendService friendService,
        UserService userService,
        ILogger<RegisterService> logger,
        ValourDb db, 
        MessageService messageService)
    {
        _memberService = memberService;
        _channelService = channelService;
        _friendService = friendService;
        _userService = userService;
        _logger = logger;
        _db = db;
        _messageService = messageService;
    }
    
    public async Task<TaskResult<User>> RegisterUserAsync(RegisterUserRequest request, HttpContext ctx, bool skipEmail = false, long? forceId = null)
    {
        var now = DateTime.Today;
        var age = now.Year - request.DateOfBirth.Year;
        if (request.DateOfBirth > now.AddYears(-age)) age--;

        if (age < 13)
            return new TaskResult<User>(false, "You must be 13 to use Valour. Sorry!");
        
        var existingInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.Email.ToLower() == request.Email);
        if (existingInfo != null)
        {
            if (!existingInfo.Verified)
                return new(false, "EMAIL_NOT_VERIFIED");
            return new(false, "This email has already been used");
        }

        var emailValid = UserUtils.TestEmail(request.Email);
        if (!emailValid.Success)
            return new(false, emailValid.Message);

        // Use the validated/normalized email going forward
        request.Email = emailValid.Data;

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

        Valour.Database.Referral refer = null;
        if (request.Referrer != null && !string.IsNullOrWhiteSpace(request.Referrer))
        {
            request.Referrer = request.Referrer.Trim();
            var referUser = await _userService.GetByNameAndTagAsync(request.Referrer);
            if (referUser is null)
                return new(false, "Referrer not found");

            var month_refers = await _db.Referrals.CountAsync(x => x.Created > DateTime.UtcNow.AddDays(-30) && x.ReferrerId == referUser.Id);
            
            // Calculate referral reward
            // reward is halved every 5 referrals in the month
            // to prevent a streamer from wrecking the economy
            var reward = 50.0m / (1 + (month_refers / 5));
            
            refer = new Valour.Database.Referral()
            {
                ReferrerId = referUser.Id,
                Created = DateTime.UtcNow,
                Reward = reward,
            };
        }

        var salt = PasswordManager.GenerateSalt();
        var hash = PasswordManager.GetHashForPassword(request.Password, salt);

        await using var tran = await _db.Database.BeginTransactionAsync();

        User user = null;
        
        try
        {
            user = new()
            {
                Id = forceId ?? IdManager.Generate(),
                Name = request.Username,
                Tag = await _userService.GetUniqueTag(request.Username),
                TimeJoined = DateTime.UtcNow,
                TimeLastActive = DateTime.UtcNow,
                Compliance = true, // All new users should be compliant
                HasAnimatedAvatar = false,
                HasCustomAvatar = false,
                Version = 0
            };

            _db.Users.Add(user.ToDatabase());
            await _db.SaveChangesAsync();

            if (refer != null)
            {
                refer.UserId = user.Id;
                await _db.Referrals.AddAsync(refer);
                
                var referAccount = await _db.EcoAccounts.FirstOrDefaultAsync(x => x.UserId == refer.ReferrerId && x.CurrencyId == ISharedCurrency.ValourCreditsId && x.AccountType == AccountType.User);
                referAccount.BalanceValue += refer.Reward;

                _db.EcoAccounts.Update(referAccount);
            }

            UserPrivateInfo userPrivateInfo = new()
            {
                Email = request.Email,
                Verified = skipEmail || (EmailConfig.Instance.ApiKey == "fake-value"),
                UserId = user.Id,
                BirthDate = DateTime.SpecifyKind(request.DateOfBirth, DateTimeKind.Utc),
                Locality = request.Locality,
                JoinInviteCode = request.InviteCode,
                JoinSource = request.Source
            };
            
            _db.PrivateInfos.Add(userPrivateInfo.ToDatabase());

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
                AccountType = AccountType.User,
                BalanceValue = 0,
                Name = "User Global Account"
            };
        
            _db.EcoAccounts.Add(globalAccount);
            
            await _db.SaveChangesAsync();
            
            // Helper for dev environment
            if (!skipEmail && EmailConfig.Instance.ApiKey != "fake-value")
            {
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
            else
            {
                var globalAccount = await _db.EcoAccounts.FirstOrDefaultAsync(x => x.UserId == user.Id && x.CurrencyId == ISharedCurrency.ValourCreditsId && x.AccountType == AccountType.User);
                globalAccount.PlanetMemberId = onboardingMember.Data.Id;
                await _db.SaveChangesAsync();
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
            var victorDm = await _channelService.GetDirectChannelByUsersAsync(user.Id, ISharedUser.VictorUserId, true);

            var victorMessage = await _messageService.PostMessageAsync(new Message()
            {
                Content = ValourWelcome,
                TimeSent = DateTime.UtcNow,
                ChannelId = victorDm.Id,
                AuthorUserId = ISharedUser.VictorUserId,
                Fingerprint = Guid.NewGuid().ToString(),
            });

            if (!victorMessage.Success)
            {
                Console.WriteLine("-- Error sending Victor DM! --");
                Console.WriteLine(victorMessage.Message);
            }

        }
        catch (Exception e)
        {
            Console.WriteLine("-- Error doing onboarding steps! --");
            Console.WriteLine(e.Message);
        }

        return TaskResult<User>.FromData(user);
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

        string emsg = $@"<body style='font-family: Ubuntu, Arial, sans-serif; margin: 0; padding: 0; background-color: #f4f4f4;'>
            <div style='max-width: 600px; margin: 20px auto; background-color: #fff; padding: 20px; border-radius: 5px; box-shadow: 0 0 10px rgba(0, 0, 0, 0.1);'>
                <img src='https://valour.gg/media/logo/logo-64.png' alt='Valour Logo' style='max-width: 100%; height: auto; display: block; margin: 0 auto;'>
                <h1 style='color: #333;'>Account Verification</h1>
                <p style='color: #666;'>Hello,</p>
                <p style='color: #666;'>Thank you for creating an account with us. To verify your account, please click the button below:</p>
                <a href='{link}' style='display: inline-block; padding: 10px 20px; background-color: #3498db; color: #fff; text-decoration: none; border-radius: 3px;'>Verify Account</a>
                <p style='color: #666;'>If you are unable to click the button, you can also copy and paste the following link into your browser:</p>
                <p style='color: #666;'><a href='{link}'>{link}</a></p>
                <p style='color: #666;'>Thank you,<br>Valour Team</p>
            </div>
        </body>";

        string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\n{link}";

        var result = await EmailManager.SendEmailAsync(email, "Valour Registration", rawmsg, emsg);
        return result;
    }
}