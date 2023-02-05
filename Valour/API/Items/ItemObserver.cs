using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Models;

namespace Valour.Api.Items
{
    /// <summary>
    /// The ItemObserver class allows global events to be hooked for entire item types
    /// </summary>
    public static class ItemObserver<T> where T : Item
    {
        /// <summary>
        /// Run when any of this item type is updated
        /// </summary>
        public static event Func<T, bool, int, Task> OnAnyUpdated;

        /// <summary>
        /// Run when any of this item type is deleted
        /// </summary>
        public static event Func<T, Task> OnAnyDeleted;

        public static async Task InvokeAnyUpdated(T updated, bool newItem, int flags)
        {
            if (OnAnyUpdated != null)
                await OnAnyUpdated?.Invoke(updated, newItem, flags);
        }

        public static async Task InvokeAnyDeleted(T deleted)
        {
            if (OnAnyDeleted != null)
                await OnAnyDeleted?.Invoke(deleted);
        }
    }
}
