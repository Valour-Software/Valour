using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Api.Items
{
    public abstract class SyncedItem<T> : Item
    {
        /// <summary>
        /// Ran when this item is updated
        /// </summary>
        public event Func<int, Task> OnUpdated;

        /// <summary>
        /// Ran when this item is deleted
        /// </summary>
        public event Func<Task> OnDeleted;

        /// <summary>
        /// Run when any of this item type is updated
        /// </summary>
        public static event Func<T, int, Task> OnAnyUpdated;

        /// <summary>
        /// Run when any of this item type is deleted
        /// </summary>
        public static event Func<T, Task> OnAnyDeleted;

        public ulong Id { get; set; }

        public virtual async Task OnUpdate(int flags)
        {

        }

        public async Task InvokeUpdated(int flags)
        {
            await OnUpdate(flags);

            if (OnUpdated != null)
                await OnUpdated?.Invoke(flags);
        }

        public async Task InvokeDeleted()
        {
            if (OnDeleted != null)
                await OnDeleted?.Invoke();
        }

        public async static Task InvokeAnyUpdated(T updated, int flags)
        {
            if (OnAnyUpdated != null)
                await OnAnyUpdated?.Invoke(updated, flags);
        }

        public async Task InvokeAnyDeleted(T deleted)
        {
            if (OnAnyDeleted != null)
                await OnAnyDeleted?.Invoke(deleted);
        }
    }
}
