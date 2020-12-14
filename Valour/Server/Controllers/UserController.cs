using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Users;
using Valour.Shared;
using Valour.Shared.Users;

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Provides routes for user-related functions on the server side.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class UserController
    {
        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB context;

        // Dependency injection
        public UserController(ValourDB context)
        {
            this.context = context;
        }

        /// <summary>
        /// Registers a new user and adds them to the database
        /// </summary>
        public async Task<TaskResult> RegisterUser(string username, string email, string password)
        {
            // Ensure unique username
            if (await context.Users.AnyAsync(x => x.Username.ToLower() == username.ToLower()))
            {
                return new TaskResult(false, $"Failed: There was already a user named {username}");
            }

            // Ensure unique email
            if (await context.Users.AnyAsync(x => x.Email.ToLower() == email.ToLower()))
            {
                return new TaskResult(false, $"Failed: There was already a user using the email {email}");
            }

            // Test password complexity
            TaskResult passwordResult = PasswordManager.TestComplexity(password);

            // Enforce password tests
            if (!passwordResult.Success)
            {
                return passwordResult;
            }

            // At this point the safety checks are complete

            // Generate random salt
            byte[] salt = new byte[32];
            PasswordManager.GenerateSalt(salt);

            // Generate password hash
            byte[] hash = PasswordManager.GetHashForPassword(password, salt);

            // Create user object
            User user = new User()
            {
                Username = username,
                Password_Hash = hash,
                Salt = salt,
                Join_DateTime = DateTime.UtcNow,
                Email = email,
                Verified_Email = false
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();
            }
            catch(System.Exception e)
            {
                return new TaskResult(false, $"A critical error occured.");
            }

            return new TaskResult(true, $"Successfully created user {username}");
        }

        /// <summary>
        /// Registers a new bot and adds them to the database
        /// </summary>
        //public async Task<string> RegisterBot(string username, ulong owner_id, string password)
        //{
        //   TODO
        //}
    }
}
