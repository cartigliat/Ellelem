// CollectionHelper.cs
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows; // Make sure this import is here
using System.Windows.Threading;

namespace ollamidesk.Common.MVVM
{
    /// <summary>
    /// Helper class for thread-safe operations on observable collections
    /// </summary>
    public static class CollectionHelper
    {
        /// <summary>
        /// Updates a collection in a thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="updateAction">The action to perform on the collection</param>
        public static void UpdateSafely<T>(ObservableCollection<T> collection, Action<ObservableCollection<T>> updateAction)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));

            // Use the fully qualified name for Application to avoid ambiguity
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                // We're on the UI thread, perform the update directly
                updateAction(collection);
            }
            else
            {
                // We're not on the UI thread, invoke the update on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() => updateAction(collection));
            }
        }

        /// <summary>
        /// Adds an item to a collection in a thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="item">The item to add</param>
        public static void AddSafely<T>(ObservableCollection<T> collection, T item)
        {
            UpdateSafely(collection, c => c.Add(item));
        }

        /// <summary>
        /// Removes an item from a collection in a thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="item">The item to remove</param>
        public static void RemoveSafely<T>(ObservableCollection<T> collection, T item)
        {
            UpdateSafely(collection, c => c.Remove(item));
        }

        /// <summary>
        /// Clears a collection in a thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to clear</param>
        public static void ClearSafely<T>(ObservableCollection<T> collection)
        {
            UpdateSafely(collection, c => c.Clear());
        }

        /// <summary>
        /// Removes an item at the specified index in a thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="index">The index of the item to remove</param>
        public static void RemoveAtSafely<T>(ObservableCollection<T> collection, int index)
        {
            UpdateSafely(collection, c => c.RemoveAt(index));
        }
    }
}