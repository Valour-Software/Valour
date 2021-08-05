using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Valour.Server.Planets;

namespace Valour.Server.Database
{
    public class ValourDBInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var members = eventData.Context.ChangeTracker.Entries().Where(x => x.Entity.GetType() == typeof(ServerPlanetMember));

            foreach (var m in members)
            {
                if (m.State == Microsoft.EntityFrameworkCore.EntityState.Added || 
                    m.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
                {
                     // work on this later for magic
                }
            }

            

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
