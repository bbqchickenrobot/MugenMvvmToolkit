﻿#region Copyright

// ****************************************************************************
// <copyright file="SuspensionManager.cs">
// Copyright (c) 2012-2017 Vyacheslav Volkov
// </copyright>
// ****************************************************************************
// <author>Vyacheslav Volkov</author>
// <email>vvs0205@outlook.com</email>
// <project>MugenMvvmToolkit</project>
// <web>https://github.com/MugenMvvmToolkit/MugenMvvmToolkit</web>
// <license>
// See license.txt in this solution or http://opensource.org/licenses/MS-PL
// </license>
// ****************************************************************************

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MugenMvvmToolkit.Interfaces;

namespace MugenMvvmToolkit.UWP
{
    public static class SuspensionManager
    {
        #region Fields

        private const string NavigationKey = "Navigation";
        private const string SessionStateFilename = "_sessionState.xml";

        private static readonly DependencyProperty FrameSessionStateKeyProperty =
            DependencyProperty.RegisterAttached("_FrameSessionStateKey", typeof(string), typeof(SuspensionManager),
                null);

        private static readonly DependencyProperty FrameSessionBaseKeyProperty =
            DependencyProperty.RegisterAttached("_FrameSessionBaseKeyParams", typeof(string),
                typeof(SuspensionManager), null);

        private static readonly DependencyProperty FrameSessionStateProperty =
            DependencyProperty.RegisterAttached("_FrameSessionState", typeof(Dictionary<string, object>),
                typeof(SuspensionManager), null);

        private static readonly List<WeakReference<Frame>> RegisteredFrames = new List<WeakReference<Frame>>();

        #endregion

        #region Properties

        /// <summary>
        ///     Provides access to global session state for the current session.  This state is
        ///     serialized by <see cref="SaveAsync" /> and restored by
        ///     <see cref="RestoreAsync" />, so values must be serializable by        
        /// </summary>
        public static Dictionary<string, object> SessionState { get; private set; } = new Dictionary<string, object>();

        #endregion

        #region Methods

        /// <summary>
        ///     Save the current <see cref="SessionState" />.  Any <see cref="Frame" /> instances
        ///     registered with <see cref="RegisterFrame" /> will also preserve their current
        ///     navigation stack, which in turn gives their active <see cref="Page" /> an opportunity
        ///     to save its state.
        /// </summary>
        /// <returns>An asynchronous task that reflects when session state has been saved.</returns>
        public static async Task SaveAsync()
        {
            try
            {
                // Save the navigation state for all registered frames
                foreach (var weakFrameReference in RegisteredFrames)
                {
                    Frame frame;
                    if (weakFrameReference.TryGetTarget(out frame))
                        SaveFrameNavigationState(frame);
                }

                // Serialize the session state synchronously to avoid asynchronous access to shared state
                var sessionData = ServiceProvider.Get<ISerializer>().Serialize(SessionState);

                // Get an output stream for the SessionState file and write the state asynchronously
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(SessionStateFilename, CreationCollisionOption.ReplaceExisting);
                using (var fileStream = await file.OpenStreamForWriteAsync())
                {
                    sessionData.Seek(0, SeekOrigin.Begin);
                    await sessionData.CopyToAsync(fileStream);
                }
            }
            catch (Exception e)
            {
                Tracer.Error(e.Flatten(true));
                throw new SuspensionManagerException(e);
            }
        }

        /// <summary>
        ///     Restores previously saved <see cref="SessionState" />.  Any <see cref="Frame" /> instances
        ///     registered with <see cref="RegisterFrame" /> will also restore their prior navigation
        ///     state, which in turn gives their active <see cref="Page" /> an opportunity restore its
        ///     state.
        /// </summary>
        /// <param name="sessionBaseKey">
        ///     An optional key that identifies the type of session.
        ///     This can be used to distinguish between multiple application launch scenarios.
        /// </param>
        /// <returns>
        ///     An asynchronous task that reflects when session state has been read.  The
        ///     content of <see cref="SessionState" /> should not be relied upon until this task
        ///     completes.
        /// </returns>
        public static async Task RestoreAsync(string sessionBaseKey = null)
        {
            SessionState = new Dictionary<string, object>();

            try
            {
                // Get the input stream for the SessionState file
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(SessionStateFilename);
                using (var inStream = await file.OpenSequentialReadAsync())
                {
                    // Deserialize the Session State                                        
                    SessionState = (Dictionary<string, object>)ServiceProvider.Get<ISerializer>().Deserialize(inStream.AsStreamForRead());
                }

                // Restore any registered frames to their saved state
                foreach (var weakFrameReference in RegisteredFrames)
                {
                    Frame frame;
                    if (weakFrameReference.TryGetTarget(out frame) &&
                        (string)frame.GetValue(FrameSessionBaseKeyProperty) == sessionBaseKey)
                    {
                        frame.ClearValue(FrameSessionStateProperty);
                        RestoreFrameNavigationState(frame);
                    }
                }
            }
            catch (Exception e)
            {
                Tracer.Error(e.Flatten(true));
                throw new SuspensionManagerException(e);
            }
        }

        /// <summary>
        ///     Registers a <see cref="Frame" /> instance to allow its navigation history to be saved to
        ///     and restored from <see cref="SessionState" />.  Frames should be registered once
        ///     immediately after creation if they will participate in session state management.  Upon
        ///     registration if state has already been restored for the specified key
        ///     the navigation history will immediately be restored.  Subsequent invocations of
        ///     <see cref="RestoreAsync" /> will also restore navigation history.
        /// </summary>
        /// <param name="frame">
        ///     An instance whose navigation history should be managed by
        ///     <see cref="SuspensionManager" />
        /// </param>
        /// <param name="sessionStateKey">
        ///     A unique key into <see cref="SessionState" /> used to
        ///     store navigation-related information.
        /// </param>
        /// <param name="sessionBaseKey">
        ///     An optional key that identifies the type of session.
        ///     This can be used to distinguish between multiple application launch scenarios.
        /// </param>
        public static void RegisterFrame(Frame frame, string sessionStateKey, string sessionBaseKey = null)
        {
            if (frame.GetValue(FrameSessionStateKeyProperty) != null)
                throw new InvalidOperationException("Frames can only be registered to one session state key");

            if (frame.GetValue(FrameSessionStateProperty) != null)
            {
                throw new InvalidOperationException(
                    "Frames must be either be registered before accessing frame session state, or not registered at all");
            }

            if (!string.IsNullOrEmpty(sessionBaseKey))
            {
                frame.SetValue(FrameSessionBaseKeyProperty, sessionBaseKey);
                sessionStateKey = sessionBaseKey + "_" + sessionStateKey;
            }

            // Use a dependency property to associate the session key with a frame, and keep a list of frames whose
            // navigation state should be managed
            frame.SetValue(FrameSessionStateKeyProperty, sessionStateKey);
            RegisteredFrames.Add(new WeakReference<Frame>(frame));

            // Check to see if navigation state can be restored
            RestoreFrameNavigationState(frame);
        }

        /// <summary>
        ///     Disassociates a <see cref="Frame" /> previously registered by <see cref="RegisterFrame" />
        ///     from <see cref="SessionState" />.  Any navigation state previously captured will be
        ///     removed.
        /// </summary>
        /// <param name="frame">
        ///     An instance whose navigation history should no longer be
        ///     managed.
        /// </param>
        public static void UnregisterFrame(Frame frame)
        {
            // Remove session state and remove the frame from the list of frames whose navigation
            // state will be saved (along with any weak references that are no longer reachable)
            SessionState.Remove((string)frame.GetValue(FrameSessionStateKeyProperty));
            RegisteredFrames.RemoveAll(weakFrameReference =>
            {
                Frame testFrame;
                return !weakFrameReference.TryGetTarget(out testFrame) || testFrame == frame;
            });
        }

        /// <summary>
        ///     Provides storage for session state associated with the specified <see cref="Frame" />.
        ///     Frames that have been previously registered with <see cref="RegisterFrame" /> have
        ///     their session state saved and restored automatically as a part of the global
        ///     <see cref="SessionState" />.  Frames that are not registered have transient state
        ///     that can still be useful when restoring pages that have been discarded from the
        ///     navigation cache.
        /// </summary>
        /// <remarks>
        ///     Apps may choose to rely on <see cref="NavigationHelper" /> to manage
        ///     page-specific state instead of working with frame session state directly.
        /// </remarks>
        /// <param name="frame">The instance for which session state is desired.</param>
        /// <returns>
        ///     A collection of state subject to the same serialization mechanism as
        ///     <see cref="SessionState" />.
        /// </returns>
        public static Dictionary<string, object> SessionStateForFrame(Frame frame)
        {
            var frameState = (Dictionary<string, object>)frame.GetValue(FrameSessionStateProperty);

            if (frameState == null)
            {
                var frameSessionKey = (string)frame.GetValue(FrameSessionStateKeyProperty);
                if (frameSessionKey == null)
                {
                    // Frames that aren't registered have transient state
                    frameState = new Dictionary<string, object>();
                }
                else
                {
                    // Registered frames reflect the corresponding session state
                    object value;
                    if (!SessionState.TryGetValue(frameSessionKey, out value))
                    {
                        value = new Dictionary<string, object>();
                        SessionState[frameSessionKey] = value;
                    }
                    frameState = (Dictionary<string, object>)value;
                }
                frame.SetValue(FrameSessionStateProperty, frameState);
            }
            return frameState;
        }

        private static void RestoreFrameNavigationState(Frame frame)
        {
            var frameState = SessionStateForFrame(frame);
            object value;
            if (frameState.TryGetValue(NavigationKey, out value))
                frame.SetNavigationState((string)value);
        }

        private static void SaveFrameNavigationState(Frame frame)
        {
            var frameState = SessionStateForFrame(frame);
            frameState[NavigationKey] = frame.GetNavigationState();
        }

        #endregion
    }

    public class SuspensionManagerException : Exception
    {
        #region Constructors

        public SuspensionManagerException()
        {
        }

        public SuspensionManagerException(Exception e)
            : base("SuspensionManager failed", e)
        {
        }

        #endregion
    }
}