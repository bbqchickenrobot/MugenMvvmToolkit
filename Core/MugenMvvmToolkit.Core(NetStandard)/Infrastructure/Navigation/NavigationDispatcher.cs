﻿#region Copyright

// ****************************************************************************
// <copyright file="NavigationDispatcher.cs">
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
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MugenMvvmToolkit.Attributes;
using MugenMvvmToolkit.DataConstants;
using MugenMvvmToolkit.Infrastructure.Callbacks;
using MugenMvvmToolkit.Interfaces.Callbacks;
using MugenMvvmToolkit.Interfaces.Models;
using MugenMvvmToolkit.Interfaces.Navigation;
using MugenMvvmToolkit.Interfaces.ViewModels;
using MugenMvvmToolkit.Models;
using MugenMvvmToolkit.Models.EventArg;

namespace MugenMvvmToolkit.Infrastructure.Navigation
{
    public class NavigationDispatcher : INavigationDispatcher
    {
        #region Nested types

        protected sealed class OpenedViewModelInfo : IOpenedViewModelInfo
        {
            #region Constructors

            public OpenedViewModelInfo(IViewModel viewModel, object provider, NavigationType navigationType)
            {
                Should.NotBeNull(viewModel, nameof(viewModel));
                ViewModel = viewModel;
                NavigationProvider = provider;
                NavigationType = navigationType;
            }

            #endregion

            #region Properties

            public IViewModel ViewModel { get; }

            public object NavigationProvider { get; }

            public NavigationType NavigationType { get; }

            #endregion
        }

        protected sealed class WeakOpenedViewModelInfo
        {
            #region Fields

            private readonly WeakReference _viewModelReference;
            private readonly WeakReference _providerReference;

            #endregion

            #region Constructors

            public WeakOpenedViewModelInfo(IViewModel viewModel, object provider, NavigationType navigationType)
            {
                _viewModelReference = ServiceProvider.WeakReferenceFactory(viewModel);
                _providerReference = ServiceProvider.WeakReferenceFactory(provider);
                NavigationType = navigationType;
            }

            #endregion

            #region Methods

            public IOpenedViewModelInfo ToOpenedViewModelInfo()
            {
                var viewModel = ViewModel;
                var provider = NavigationProvider;
                if (viewModel == null)
                    return null;
                return new OpenedViewModelInfo(viewModel, provider, NavigationType);
            }

            #endregion

            #region Properties

            public IViewModel ViewModel => (IViewModel)_viewModelReference.Target;

            public object NavigationProvider => _providerReference.Target;

            public NavigationType NavigationType { get; }

            #endregion
        }

        #endregion

        #region Fields

        protected readonly Dictionary<NavigationType, List<WeakOpenedViewModelInfo>> OpenedViewModels;

        #endregion

        #region Constructors

        [Preserve(Conditional = true)]
        public NavigationDispatcher([NotNull] IOperationCallbackManager callbackManager)
        {
            Should.NotBeNull(callbackManager, nameof(callbackManager));
            CallbackManager = callbackManager;
            OpenedViewModels = new Dictionary<NavigationType, List<WeakOpenedViewModelInfo>>();
        }

        #endregion

        #region Properties

        protected IOperationCallbackManager CallbackManager { get; }

        #endregion

        #region Methods

        [NotNull]
        protected virtual IEnumerable<NavigationType> GetOpenedNavigationTypes([NotNull] IDataContext context)
        {
            lock (OpenedViewModels)
                return OpenedViewModels.Keys.ToArrayEx();
        }

        [NotNull]
        protected virtual IList<IOpenedViewModelInfo> GetOpenedViewModelsInternal([NotNull] NavigationType type, [NotNull] IDataContext context)
        {
            lock (OpenedViewModels)
            {
                List<WeakOpenedViewModelInfo> list;
                if (!OpenedViewModels.TryGetValue(type, out list))
                    return Empty.Array<IOpenedViewModelInfo>();
                var result = new List<IOpenedViewModelInfo>();
                for (int i = 0; i < list.Count; i++)
                {
                    var target = list[i].ToOpenedViewModelInfo();
                    if (target == null)
                    {
                        list.RemoveAt(i);
                        --i;
                    }
                    else
                        result.Add(target);
                }
                if (result.Count == 0)
                    OpenedViewModels.Remove(type);
                return result;
            }
        }

        protected virtual void UpdateOpenedViewModelsInternal([NotNull]NavigationType type, [NotNull] IList<IOpenedViewModelInfo> viewModelInfos, [NotNull] IDataContext context)
        {
            lock (OpenedViewModels)
            {
                if (viewModelInfos.Count == 0)
                    OpenedViewModels.Remove(type);
                else
                    OpenedViewModels[type] = viewModelInfos.Select(info => new WeakOpenedViewModelInfo(info.ViewModel, info.NavigationProvider, info.NavigationType)).ToList();
            }
        }

        protected virtual void HandleOpenedViewModels(INavigationContext context)
        {
            var viewModelFrom = context.GetData(NavigationConstants.DoNotTrackViewModelFrom) ? null : context.ViewModelFrom;
            var viewModelTo = context.GetData(NavigationConstants.DoNotTrackViewModelTo) ? null : context.ViewModelTo;
            lock (OpenedViewModels)
            {
                List<WeakOpenedViewModelInfo> list;
                if (!OpenedViewModels.TryGetValue(context.NavigationType, out list))
                {
                    list = new List<WeakOpenedViewModelInfo>();
                    OpenedViewModels[context.NavigationType] = list;
                }
                if (viewModelTo != null && (context.NavigationMode == NavigationMode.Refresh || context.NavigationMode == NavigationMode.Back || context.NavigationMode == NavigationMode.New))
                {
                    WeakOpenedViewModelInfo viewModelRef = null;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var target = list[i].ViewModel;
                        if (target == null || ReferenceEquals(target, viewModelTo))
                        {
                            if (target != null)
                                viewModelRef = list[i];
                            list.RemoveAt(i);
                            --i;
                        }
                    }
                    if (viewModelRef == null)
                        viewModelRef = new WeakOpenedViewModelInfo(viewModelTo, context.NavigationProvider, context.NavigationType);
                    list.Add(viewModelRef);
                }
                if (viewModelFrom != null && context.NavigationMode.IsClose())
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var target = list[i].ViewModel;
                        if (target == null || ReferenceEquals(target, viewModelFrom))
                        {
                            list.RemoveAt(i);
                            --i;
                        }
                    }
                }
            }
        }

        protected virtual Task<bool> OnNavigatingInternalAsync(INavigationContext context)
        {
            bool data;
            if (context.TryGetData(NavigationConstants.ImmediateClose, out data) && data)
                return Empty.TrueTask;
            bool isClose = context.NavigationMode.IsClose() && context.ViewModelFrom != null;
            var navigatingTask = OnNavigatingFromAsync(context) ?? Empty.TrueTask;
            if (!isClose)
                return navigatingTask;

            if (navigatingTask.IsCompleted)
            {
                if (navigatingTask.Result)
                    return OnClosingAsync(context.ViewModelFrom, context);
                return Empty.FalseTask;
            }
            return navigatingTask
                .TryExecuteSynchronously(task =>
                {
                    if (task.Result)
                        return OnClosingAsync(context.ViewModelFrom, context);
                    return Empty.FalseTask;
                }).Unwrap();
        }

        protected virtual Task<bool> OnNavigatingFromAsync(INavigationContext context)
        {
            return (context.ViewModelFrom as INavigableViewModel)?.OnNavigatingFromAsync(context);
        }

        protected virtual void OnNavigatedInternal(INavigationContext context)
        {
            (context.ViewModelFrom as INavigableViewModel)?.OnNavigatedFrom(context);
            (context.ViewModelTo as INavigableViewModel)?.OnNavigatedTo(context);
            if (context.NavigationMode.IsClose() && context.ViewModelFrom != null)
            {
                OnClosed(context.ViewModelFrom, context);
                if (context.NavigationType.Operation != null && !context.GetData(NavigationConstants.SuppressNavigationCallbackOnClose))
                {
                    var operationResult = OperationResult.CreateResult<object>(context.NavigationType.Operation, context.ViewModelFrom, null, context);
                    CallbackManager.SetResult(operationResult);
                }
            }
        }

        protected virtual void OnNavigationFailedInternal(INavigationContext context, Exception exception)
        {
            if (exception != null)
                Tracer.Error(exception.Flatten(true));
            var viewModel = context.NavigationMode.IsCloseOrBackground() ? context.ViewModelFrom : context.ViewModelTo;
            if (viewModel != null && context.NavigationType.Operation != null)
                CallbackManager.SetResult(OperationResult.CreateErrorResult<object>(context.NavigationType.Operation, viewModel, exception, context));
        }

        protected virtual void OnNavigationCanceledInternal(INavigationContext context)
        {
            var viewModel = context.NavigationMode.IsCloseOrBackground() ? context.ViewModelFrom : context.ViewModelTo;
            if (viewModel != null && context.NavigationType.Operation != null)
                CallbackManager.SetResult(OperationResult.CreateCancelResult<object>(context.NavigationType.Operation, viewModel, context));
        }

        protected virtual void RaiseNavigated(INavigationContext context, bool isCanceled, Exception exception)
        {
            Navigated?.Invoke(this, new NavigatedEventArgs(context, isCanceled, exception));
        }

        protected virtual Task<bool> OnClosingAsync(IViewModel viewModel, IDataContext context)
        {
            var handler = viewModel.Settings.Metadata.GetData(ViewModelConstants.ClosingEvent);
            var closingTask = (viewModel as ICloseableViewModel)?.OnClosingAsync(context) ?? Empty.TrueTask;
            if (handler == null)
                return closingTask;
            if (closingTask.IsCompleted)
            {
                var args = new ViewModelClosingEventArgs(viewModel, context);
                handler(viewModel, args);
                return args.GetCanCloseAsync();
            }
            return closingTask.TryExecuteSynchronously(task =>
            {
                if (!task.Result)
                    return Empty.FalseTask;
                var args = new ViewModelClosingEventArgs(viewModel, context);
                handler(viewModel, args);
                return args.GetCanCloseAsync();
            }).Unwrap();
        }

        protected virtual void OnClosed(IViewModel viewModel, IDataContext context)
        {
            (viewModel as ICloseableViewModel)?.OnClosed(context);
            viewModel.Settings.Metadata.GetData(ViewModelConstants.ClosedEvent)?.Invoke(viewModel, new ViewModelClosedEventArgs(viewModel, context));
        }

        private static void Trace(string navigationName, INavigationContext context)
        {
            if (Tracer.TraceInformation)
                Tracer.Info($"{navigationName}({context.NavigationMode}) from '{context.ViewModelFrom}' to '{context.ViewModelTo}', type '{context.NavigationType}'");
        }

        #endregion

        #region Implementation of interfaces

        public event EventHandler<INavigationDispatcher, NavigatedEventArgs> Navigated;

        public void UpdateOpenedViewModels(NavigationType type, IList<IOpenedViewModelInfo> viewModelInfos, IDataContext context = null)
        {
            Should.NotBeNull(type, nameof(type));
            Should.NotBeNull(viewModelInfos, nameof(viewModelInfos));
            UpdateOpenedViewModelsInternal(type, viewModelInfos, context ?? DataContext.Empty);
        }

        public Task<bool> OnNavigatingAsync(INavigationContext context)
        {
            Should.NotBeNull(context, nameof(context));
            Trace(nameof(OnNavigatingAsync), context);
            return OnNavigatingInternalAsync(context);
        }

        public void OnNavigated(INavigationContext context)
        {
            Should.NotBeNull(context, nameof(context));
            HandleOpenedViewModels(context);
            OnNavigatedInternal(context);
            RaiseNavigated(context, false, null);
            Trace(nameof(OnNavigated), context);
        }

        public void OnNavigationFailed(INavigationContext context, Exception exception)
        {
            Should.NotBeNull(context, nameof(context));
            Should.NotBeNull(exception, nameof(exception));
            OnNavigationFailedInternal(context, exception);
            RaiseNavigated(context, false, exception);
            Trace(nameof(OnNavigationFailed), context);
        }

        public void OnNavigationCanceled(INavigationContext context)
        {
            Should.NotBeNull(context, nameof(context));
            OnNavigationCanceledInternal(context);
            RaiseNavigated(context, true, null);
            Trace(nameof(OnNavigationCanceled), context);
        }

        public IDictionary<NavigationType, IList<IOpenedViewModelInfo>> GetOpenedViewModels(IDataContext context = null)
        {
            if (context == null)
                context = DataContext.Empty;
            var navigationTypes = GetOpenedNavigationTypes(context);
            var dictionary = new Dictionary<NavigationType, IList<IOpenedViewModelInfo>>();
            foreach (var navigationType in navigationTypes)
            {
                var viewModels = GetOpenedViewModelsInternal(navigationType, context);
                if (viewModels.Count != 0)
                    dictionary[navigationType] = viewModels;
            }
            return dictionary;
        }

        public IList<IOpenedViewModelInfo> GetOpenedViewModels(NavigationType type, IDataContext context = null)
        {
            Should.NotBeNull(type, nameof(type));
            return GetOpenedViewModelsInternal(type, context ?? DataContext.Empty);
        }

        #endregion
    }
}