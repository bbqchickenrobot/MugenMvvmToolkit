#region Copyright

// ****************************************************************************
// <copyright file="MvvmFragmentMediator.cs">
// Copyright (c) 2012-2015 Vyacheslav Volkov
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
using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;
using Android.Util;
using Android.Views;
using JetBrains.Annotations;
using MugenMvvmToolkit.Android.Binding;
using MugenMvvmToolkit.Android.Views;
using MugenMvvmToolkit.Binding;
using MugenMvvmToolkit.DataConstants;
using MugenMvvmToolkit.Interfaces.Models;
using MugenMvvmToolkit.Interfaces.ViewModels;
using MugenMvvmToolkit.Models;
#if APPCOMPAT
using MugenMvvmToolkit.Android.AppCompat.Interfaces.Views;
using MugenMvvmToolkit.Android.AppCompat.Infrastructure.Presenters;
using MugenMvvmToolkit.Android.AppCompat.Interfaces.Mediators;
using MugenMvvmToolkit.Android.Infrastructure;
using MugenMvvmToolkit.Android.Infrastructure.Mediators;
using Fragment = Android.Support.V4.App.Fragment;
using DialogFragment = Android.Support.V4.App.DialogFragment;

namespace MugenMvvmToolkit.Android.AppCompat.Infrastructure.Mediators
#else
using MugenMvvmToolkit.Android.Interfaces.Mediators;
using MugenMvvmToolkit.Android.Infrastructure.Presenters;
using MugenMvvmToolkit.Android.Interfaces.Views;

namespace MugenMvvmToolkit.Android.Infrastructure.Mediators
#endif
{
    public class MvvmFragmentMediator : MediatorBase<Fragment>, IMvvmFragmentMediator
    {
        #region Nested types

        private sealed class DialogInterfaceOnKeyListener : Java.Lang.Object, IDialogInterfaceOnKeyListener
        {
            #region Fields

            private readonly MvvmFragmentMediator _mediator;

            #endregion

            #region Constructors

            public DialogInterfaceOnKeyListener(MvvmFragmentMediator mediator)
            {
                _mediator = mediator;
            }

            #endregion

            #region Implementation of IDialogInterfaceOnKeyListener

            bool IDialogInterfaceOnKeyListener.OnKey(IDialogInterface dialog, Keycode keyCode, KeyEvent e)
            {
                if (keyCode != Keycode.Back || e.Action != KeyEventActions.Up)
                    return false;
                var dialogFragment = _mediator.Target as DialogFragment;
                if (dialogFragment != null)
                    dialogFragment.Dismiss();
                return true;
            }

            #endregion
        }

        #endregion

        #region Fields

        private DialogInterfaceOnKeyListener _keyListener;
        private View _view;
        private bool _removed;
        private bool _isPreferenceContext;

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="MvvmFragmentMediator" /> class.
        /// </summary>
        public MvvmFragmentMediator([NotNull] Fragment target)
            : base(target)
        {
            CacheFragmentView = PlatformExtensions.CacheFragmentViewDefault;
        }

        #endregion

        #region Implementation of IMvvmFragmentMediator

        /// <summary>
        ///     Gets the <see cref="Fragment" />.
        /// </summary>
        Fragment IMvvmFragmentMediator.Fragment
        {
            get { return Target; }
        }

        /// <summary>
        ///     Gets or sets that is responsible for cache view in fragment.
        /// </summary>
        public bool CacheFragmentView { get; set; }

        /// <summary>
        ///     Called when a fragment is first attached to its activity.
        /// </summary>
        public virtual void OnAttach(Activity activity, Action<Activity> baseOnAttach)
        {
            baseOnAttach(activity);
        }

        /// <summary>
        ///     Initialize the contents of the Activity's standard options menu.
        /// </summary>
        public virtual void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater,
            Action<IMenu, MenuInflater> baseOnCreateOptionsMenu)
        {
            if (Target.Activity == null || Target.View == null)
                baseOnCreateOptionsMenu(menu, inflater);
            else
            {
                var optionsMenu = Target.View.FindViewById<OptionsMenu>(Resource.Id.OptionsMenu);
                if (optionsMenu != null)
                    optionsMenu.Inflate(Target.Activity, menu);
            }
        }

        /// <summary>
        ///     Called to have the fragment instantiate its user interface view.
        /// </summary>
        public virtual View OnCreateView(int? viewId, LayoutInflater inflater, ViewGroup container,
            Bundle savedInstanceState, Func<LayoutInflater, ViewGroup, Bundle, View> baseOnCreateView)
        {
            if (_removed)
                return null;
            if (CacheFragmentView && _view != null)
            {
                _view.RemoveFromParent();
                return _view;
            }
            _view.ClearBindingsRecursively(true, true);
            if (viewId.HasValue)
            {
                _view = inflater.ToBindableLayoutInflater().Inflate(viewId.Value, container, false);
                PlatformExtensions.FragmentViewMember.SetValue(_view, Target);
                _view.SetDataContext(DataContext);
                return _view;
            }
            return baseOnCreateView(inflater, container, savedInstanceState);
        }

        /// <summary>
        ///     Called when the target is starting.
        /// </summary>
        public virtual void OnCreate(Bundle savedInstanceState, Action<Bundle> baseOnCreate)
        {
            if (Tracer.TraceInformation)
                Tracer.Info("OnCreate fragment({0})", Target);
            OnCreate(savedInstanceState);
            baseOnCreate(savedInstanceState);

            var viewModel = DataContext as IViewModel;
            if (viewModel != null)
            {
                if (!viewModel.Settings.Metadata.Contains(ViewModelConstants.StateNotNeeded) && !viewModel.Settings.Metadata.Contains(ViewModelConstants.StateManager))
                    viewModel.Settings.Metadata.AddOrUpdate(ViewModelConstants.StateManager, this);
                viewModel.Settings.Metadata.AddOrUpdate(PlatformExtensions.FragmentConstant, Target);
            }
            else if (DataContext == null)
            {
                if (savedInstanceState != null && savedInstanceState.ContainsKey(IgnoreStateKey))
                {
                    _removed = true;
                    Target.FragmentManager
                        .BeginTransaction()
                        .Remove(Target)
                        .CommitAllowingStateLoss();
                }
#if !APPCOMPAT
                else if (Target is PreferenceFragment)
                {
                    var activity = Target.Activity as PreferenceActivity;
                    if (activity != null)
                    {
                        _isPreferenceContext = true;
                        Target.Bind(AttachedMembers.Object.DataContext)
                            .To(activity, AttachedMembers.Object.DataContext)
                            .Build();
                    }
                }
#endif
            }
        }

        /// <summary>
        ///     Called immediately after <c>OnCreateView(Android.Views.LayoutInflater, Android.Views.ViewGroup, Android.Views.ViewGroup)</c> has returned, but before any saved state has been restored in to the view.
        /// </summary>
        public virtual void OnViewCreated(View view, Bundle savedInstanceState, Action<View, Bundle> baseOnViewCreated)
        {
            var dialogFragment = Target as DialogFragment;
            if (dialogFragment == null)
                PlatformExtensions.NotifyActivityAttached(Target.Activity, view);
            else
            {
                var dialog = dialogFragment.Dialog;
                if (dialog != null)
                {
                    TrySetTitleBinding();
                    if (_keyListener == null)
                        _keyListener = new DialogInterfaceOnKeyListener(this);
                    dialog.SetOnKeyListener(_keyListener);
                }
            }
            baseOnViewCreated(view, savedInstanceState);
        }

        /// <summary>
        ///     Called when the view previously created by <c>OnCreateView</c> has been detached from the fragment.
        /// </summary>
        public virtual void OnDestroyView(Action baseOnDestroyView)
        {
            baseOnDestroyView();
            if (!CacheFragmentView)
            {
                _view.ClearBindingsRecursively(true, true);
                _view = null;
            }
        }

        /// <summary>
        ///     Gets the current preference manager.
        /// </summary>
        protected override PreferenceManager PreferenceManager
        {
            get
            {
#if APPCOMPAT
                return null;
#else
                var fragment = Target as PreferenceFragment;
                if (fragment == null)
                    return null;
                return fragment.PreferenceManager;
#endif
            }
        }

        /// <summary>
        ///     Called when the fragment is no longer in use.
        /// </summary>
        public override void OnDestroy(Action baseOnDestroy)
        {
            if (Tracer.TraceInformation)
                Tracer.Info("OnDestroy fragment({0})", Target);
            RaiseDestroy();

            _view.RemoveFromParent();
            _view.ClearBindingsRecursively(true, true);
            _view = null;

            var dialogFragment = Target as DialogFragment;
            if (dialogFragment != null)
                dialogFragment.Dialog.ClearBindings(true, true);

            if (_keyListener != null)
            {
                _keyListener.Dispose();
                _keyListener = null;
            }

            var viewModel = DataContext as IViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.Metadata.Remove(PlatformExtensions.FragmentConstant);
                object stateManager;
                if (viewModel.Settings.Metadata.TryGetData(ViewModelConstants.StateManager, out stateManager) &&
                    stateManager == this)
                    viewModel.Settings.Metadata.Remove(ViewModelConstants.StateManager);
            }

            base.OnDestroy(baseOnDestroy);
            Closing = null;
            Canceled = null;
            Destroyed = null;
        }

        /// <summary>
        ///     Called after <c>OnCreate(Android.OS.Bundle)</c> or after <c>OnRestart</c> when the activity had been stopped, but is now again being displayed to the user.
        /// </summary>
        public override void OnSaveInstanceState(Bundle outState, Action<Bundle> baseOnSaveInstanceState)
        {
            if (_isPreferenceContext)
                baseOnSaveInstanceState(outState);
            else
                base.OnSaveInstanceState(outState, baseOnSaveInstanceState);
        }

        /// <summary>
        ///     Called when the fragment is no longer attached to its activity.
        /// </summary>
        public virtual void OnDetach(Action baseOnDetach)
        {
            baseOnDetach();
            Target.ClearBindings(false, true);
        }

        /// <summary>
        ///     Called when a fragment is being created as part of a view layout
        ///     inflation, typically from setting the content view of an activity.
        /// </summary>
        public virtual void OnInflate(Activity activity, IAttributeSet attrs, Bundle savedInstanceState,
            Action<Activity, IAttributeSet, Bundle> baseOnInflate)
        {
            Target.ClearBindings(false, false);
            List<string> strings = ViewFactory.ReadStringAttributeValue(activity, attrs, Resource.Styleable.Binding, null);
            if (strings != null && strings.Count != 0)
            {
                foreach (string bind in strings)
                    BindingServiceProvider.BindingProvider.CreateBindingsFromString(Target, bind, null);
            }
            baseOnInflate(activity, attrs, savedInstanceState);
        }

        /// <summary>
        ///     Called when the Fragment is visible to the user.
        /// </summary>
        public virtual void OnStart(Action baseOnStart)
        {
            baseOnStart();
            var view = Target.View;
            if (view != null)
                view.RootView.ListenParentChange();
        }

        /// <summary>
        ///     Called when the Fragment is no longer started.
        /// </summary>
        public virtual void OnStop(Action baseOnStop)
        {
            baseOnStop();
        }

        /// <summary>
        ///     This method will be invoked when the dialog is canceled.
        /// </summary>
        public virtual void OnCancel(IDialogInterface dialog, Action<IDialogInterface> baseOnCancel)
        {
            var handler = Canceled;
            if (handler != null)
                handler((IWindowView)Target, EventArgs.Empty);
            baseOnCancel(dialog);
        }

        /// <summary>
        ///     Dismiss the fragment and its dialog.
        /// </summary>
        public virtual void Dismiss(Action baseDismiss)
        {
            if (OnClosing())
                baseDismiss();
        }

        /// <summary>
        ///     Inflates the given XML resource and adds the preference hierarchy to the current preference hierarchy.
        /// </summary>
        public virtual void AddPreferencesFromResource(Action<int> baseAddPreferencesFromResource, int preferencesResId)
        {
#if APPCOMPAT
            PreferenceFragment fragment = null;
#else
            var fragment = Target as PreferenceFragment;
#endif
            if (fragment == null)
            {
                Tracer.Error("The AddPreferencesFromResource method supported only for PreferenceFragment");
                return;
            }
            baseAddPreferencesFromResource(preferencesResId);
            InitializePreferences(fragment.PreferenceScreen, preferencesResId);
        }

        /// <summary>
        ///     Occurred on closing window.
        /// </summary>
        public virtual event EventHandler<IWindowView, CancelEventArgs> Closing;

        /// <summary>
        ///     Occurred on closed window.
        /// </summary>
        public virtual event EventHandler<IWindowView, EventArgs> Canceled;

        /// <summary>
        ///     Occurred on destroyed fragment.
        /// </summary>
        public virtual event EventHandler<Fragment, EventArgs> Destroyed;

        #endregion

        #region Overrides of MediatorBase<Fragment>

        /// <summary>
        ///     Occurs when the DataContext property changed.
        /// </summary>
        protected override void OnDataContextChanged(object oldValue, object newValue)
        {
            base.OnDataContextChanged(oldValue, newValue);
            View view = Target.View;
            if (view != null)
                view.SetDataContext(DataContext);
        }

        protected override IDataContext CreateRestorePresenterContext(Fragment target)
        {
            return new DataContext
            {
                {DynamicViewModelWindowPresenter.IsOpenViewConstant, true},
                {DynamicViewModelWindowPresenter.RestoredViewConstant, target},
                {NavigationConstants.SuppressPageNavigation, true}
            };
        }

        #endregion

        #region Methods

        private void RaiseDestroy()
        {
            var handler = Destroyed;
            if (handler != null)
                handler(Target, EventArgs.Empty);
        }

        private bool OnClosing()
        {
            var closing = Closing;
            if (closing == null)
                return true;
            var args = new CancelEventArgs();
            closing((IWindowView)Target, args);
            return !args.Cancel;
        }

        private void TrySetTitleBinding()
        {
            var hasDisplayName = DataContext as IHasDisplayName;
            var dialogFragment = Target as DialogFragment;
            if (dialogFragment == null || hasDisplayName == null)
                return;
            var dialog = dialogFragment.Dialog;
            if (dialog != null)
                BindingServiceProvider.BindingProvider.CreateBindingsFromString(dialog, "Title DisplayName", new object[] { hasDisplayName });
        }

        #endregion
    }
}