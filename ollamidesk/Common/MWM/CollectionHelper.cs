// CollectionHelper.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ollamidesk.RAG.Diagnostics;

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

            Dispatcher dispatcher = GetDispatcher();
            if (dispatcher == null)
            {
                // Fallback for cases when there's no dispatcher (unit tests, etc.)
                updateAction(collection);
                return;
            }

            if (dispatcher.CheckAccess())
            {
                // We're on the UI thread, perform the update directly
                updateAction(collection);
            }
            else
            {
                // We're not on the UI thread, invoke the update on the UI thread
                dispatcher.Invoke(() => updateAction(collection));
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

        /// <summary>
        /// Performs a batch update on a collection in an atomic, thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="newItems">New items to add to the collection</param>
        /// <param name="clearFirst">Whether to clear the collection before adding new items</param>
        public static void BatchUpdateSafely<T>(ObservableCollection<T> collection, IEnumerable<T> newItems, bool clearFirst = false)
        {
            UpdateSafely(collection, c =>
            {
                if (clearFirst)
                {
                    c.Clear();
                }

                foreach (var item in newItems)
                {
                    c.Add(item);
                }
            });
        }

        /// <summary>
        /// Replaces all items in a collection with new items in an atomic, thread-safe manner
        /// </summary>
        /// <typeparam name="T">The type of items in the collection</typeparam>
        /// <param name="collection">The collection to update</param>
        /// <param name="newItems">New items to replace the collection with</param>
        public static void ReplaceSafely<T>(ObservableCollection<T> collection, IEnumerable<T> newItems)
        {
            BatchUpdateSafely(collection, newItems, true);
        }

        /// <summary>
        /// Gets the UI dispatcher, handling cases where Application.Current might be null
        /// </summary>
        /// <returns>The UI dispatcher, or null if not available</returns>
        public static Dispatcher GetDispatcher()
        {
            if (Application.Current != null)
            {
                return Application.Current.Dispatcher;
            }

            // Fallback to the dispatcher of the current thread if available
            return Dispatcher.CurrentDispatcher;
        }

        /// <summary>
        /// Safely executes an action on the UI thread
        /// </summary>
        /// <param name="action">The action to execute</param>
        public static void ExecuteOnUIThread(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var dispatcher = GetDispatcher();
            if (dispatcher == null)
            {
                action();
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
    }

    /// <summary>
    /// Extension methods for working with tasks
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Continues a task without waiting for completion, but logs any errors that occur
        /// </summary>
        /// <param name="task">The task to continue</param>
        /// <param name="diagnostics">The diagnostics service to log errors</param>
        /// <param name="source">The source component for error logging</param>
        public static void FireAndForget(this Task task, RagDiagnosticsService diagnostics, string source = "TaskExtensions")
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            task.ContinueWith(t =>
            {
                if (t.IsFaulted && diagnostics != null && t.Exception != null)
                {
                    diagnostics.Log(DiagnosticLevel.Error, source,
                        $"Unhandled exception in background task: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}