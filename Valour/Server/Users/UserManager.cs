using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    public class UserManager
    {

        /// <summary>
        /// The result from a user validation attempt
        /// </summary>
        public class ValidateResult
        {
            /// <summary>
            /// The result for the validation
            /// </summary>
            public TaskResult Result { get; set; }

            /// <summary>
            /// The user retrieved (if any)
            /// </summary>
            public User User { get; set; }

            public ValidateResult(TaskResult result, User user)
            {
                this.Result = result;
                this.User = user;
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials (async)
        /// </summary>
        public async Task<ValidateResult> ValidateAsync(string credential_type, string identifier, string secret)
        {
            using (ValourDB context  = new ValourDB(ValourDB.DBOptions))
            {
                // Find the credential that matches the identifier and type
                Credential credential = await context.Credentials.FirstOrDefaultAsync(
                    x => string.Equals(credential_type, x.Credential_Type, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(identifier, x.Identifier, StringComparison.OrdinalIgnoreCase));

                // Use salt to validate secret hash
                byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

                if (hash != credential.Secret)
                {
                    return new ValidateResult(new TaskResult(false, "Secret validation failed."), null);
                }

                User user = await context.Users.FindAsync(credential.User_Id);

                return new ValidateResult(new TaskResult(true, "Succeeded"), user);
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials
        /// </summary>
        public ValidateResult Validate(string credential_type, string identifier, string secret)
        {
            return ValidateAsync(credential_type, identifier, secret).Result;
        }

        /// <summary>
        /// Logs the user in 
        /// </summary>
        public async Task LogInUser(HttpContext http, User user, bool isPersistant = false)
        {
            ClaimsIdentity identity = new ClaimsIdentity();
        }

        /// <summary>
        /// Returns the role claims for a given user
        /// </summary>
        private IEnumerable<Claim> GetUserRoleClaims(User user)
        {
            List<Claim> claims = new List<Claim>();
            
        }

        /// <summary>
        /// Adds the given user to the given role
        /// </summary>
        public async Task<TaskResult> AddToRole(User user, string roleCode)
        {
            using (ValourDB context = new ValourDB(ValourDB.DBOptions))
            {
                Role role = await context.Roles.FirstOrDefaultAsync(x => x.Code == roleCode);

                if (role == null)
                {
                    return new TaskResult(false, $"Could not find role with code {roleCode}.");
                }

                return await AddToRole(user, role);
            }
        }

        /// <summary>
        /// Adds a user to the given role
        /// </summary>
        public async Task<TaskResult> AddToRole(User user, Role role)
        {
            using (ValourDB context = new ValourDB(ValourDB.DBOptions))
            {
                if (await context.UserRoles.AnyAsync(x => x.User_Id == user.Id && x.Role_Id == role.Id))
                {
                    return new TaskResult(false, $"User {user.Username} already has role {role.Name}.");
                }

                UserRole userRole = new UserRole()
                {
                    User_Id = user.Id,
                    Role_Id = role.Id
                };

                await context.UserRoles.AddAsync(userRole);
                await context.SaveChangesAsync();

                return new TaskResult(true, "Successfully added to role.");
            }
        }

        /// <summary>
        /// Removes the given user from the given role
        /// </summary>
        public async Task<TaskResult> RemoveFromRole(User user, string roleCode)
        {
            using (ValourDB context = new ValourDB(ValourDB.DBOptions))
            {
                Role role = await context.Roles.FirstOrDefaultAsync(x => x.Code == roleCode);

                if (role == null)
                {
                    return new TaskResult(false, $"Could not find role with code {roleCode}.");
                }

                return await RemoveFromRole(user, role);
            }
        }

        /// <summary>
        /// Removes the given user from the given role
        /// </summary>
        public async Task<TaskResult> RemoveFromRole(User user, Role role)
        {
            using (ValourDB context = new ValourDB(ValourDB.DBOptions))
            {
                UserRole userRole = await context.UserRoles.FirstOrDefaultAsync(x => x.User_Id == user.Id && x.Role_Id == role.Id);

                if (userRole == null)
                {
                    return new TaskResult(false, $"User {user.Username} doesn't have the role {role.Name}.");
                }

                context.UserRoles.Remove(userRole);
                await context.SaveChangesAsync();

                return new TaskResult(true, "Successfully removed the role.");
            }
        }
    }
}
