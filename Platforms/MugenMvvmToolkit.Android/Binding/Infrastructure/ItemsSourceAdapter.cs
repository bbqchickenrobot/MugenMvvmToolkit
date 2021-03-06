﻿#region Copyright

// ****************************************************************************
// <copyright file="ItemsSourceAdapter.cs">
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.Lang;
using JetBrains.Annotations;
using MugenMvvmToolkit.Android.Binding.Interfaces;
using MugenMvvmToolkit.Android.Interfaces;
using MugenMvvmToolkit.Android.Interfaces.Views;
using MugenMvvmToolkit.Android.Models;
using MugenMvvmToolkit.Binding;
using Object = Java.Lang.Object;

namespace MugenMvvmToolkit.Android.Binding.Infrastructure
{
    public class ItemsSourceAdapter : BaseAdapter, IItemsSourceAdapter, IFilterable
    {
        #region Nested types

        private sealed class EmptyFilter : Filter
        {
            #region Fields

            private readonly ItemsSourceAdapter _adapter;

            #endregion

            #region Constructors

            public EmptyFilter(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
            {
            }

            public EmptyFilter(ItemsSourceAdapter adapter)
            {
                _adapter = adapter;
            }

            #endregion

            #region Methods

            protected override FilterResults PerformFiltering(ICharSequence constraint)
            {
                if (_adapter == null)
                    return new FilterResults();
                (_adapter.Container as AutoCompleteTextView)?.SetBindingMemberValue(AttachedMembers.AutoCompleteTextView.FilterText, constraint);
                return new FilterResults { Count = _adapter.Count };
            }

            protected override void PublishResults(ICharSequence constraint, FilterResults results)
            {
            }

            #endregion
        }

        #endregion

        #region Fields

        private IEnumerable _itemsSource;
        private readonly object _container;
        private readonly NotifyCollectionChangedEventHandler _weakHandler;
        private readonly ReflectionExtensions.IWeakEventHandler<EventArgs> _listener;
        private readonly LayoutInflater _layoutInflater;
        private readonly DataTemplateProvider _dropDownTemplateProvider;
        private readonly DataTemplateProvider _itemTemplateProvider;
        private readonly IStableIdProvider _stableIdProvider;
        private readonly int _defaultDropDownTemplate;

        private Dictionary<int, int> _resourceTypeToItemType;
        private int _currentTypeIndex;
        private Filter _filter;

        #endregion

        #region Constructors

        public ItemsSourceAdapter([NotNull] object container, Context context, bool listenCollectionChanges, string dropDownItemTemplateSelectorName = null,
            string itemTemplateSelectorName = null, string dropDownItemTemplateName = null, string itemTemplateName = null)
        {
            Should.NotBeNull(container, nameof(container));
            _container = container;
            container.TryGetBindingMemberValue(AttachedMembers.Object.StableIdProvider, out _stableIdProvider);
            _itemTemplateProvider = new DataTemplateProvider(container, itemTemplateName ?? AttachedMemberConstants.ItemTemplate,
                itemTemplateSelectorName ?? AttachedMemberConstants.ItemTemplateSelector);
            _dropDownTemplateProvider = new DataTemplateProvider(container,
                dropDownItemTemplateName ?? AttachedMembers.AdapterView.DropDownItemTemplate,
                dropDownItemTemplateSelectorName ?? AttachedMembers.AdapterView.DropDownItemTemplateSelector);
            _layoutInflater = context.GetBindableLayoutInflater();
            if (listenCollectionChanges)
                _weakHandler = ReflectionExtensions.MakeWeakCollectionChangedHandler(this, (adapter, o, arg3) => adapter.OnCollectionChanged(o, arg3));
            var activityView = context.GetActivity() as IActivityView;
            if (activityView != null)
            {
                _listener = ReflectionExtensions.CreateWeakEventHandler<ItemsSourceAdapter, EventArgs>(this, (adapter, o, arg3) => adapter.ActivityViewOnDestroyed((Activity)o));
                activityView.Mediator.Destroyed += _listener.Handle;
            }
            _defaultDropDownTemplate = IsSpinner()
                ? global::Android.Resource.Layout.SimpleDropDownItem1Line
                : global::Android.Resource.Layout.SimpleSpinnerDropDownItem;
            (container as AdapterView)?.SetDisableHierarchyListener(true);
        }

        #endregion

        #region Properties

        protected object Container => _container;

        protected LayoutInflater LayoutInflater => _layoutInflater;

        protected DataTemplateProvider DataTemplateProvider => _itemTemplateProvider;

        #endregion

        #region Implementation of IItemsSourceAdapter

        public virtual IEnumerable ItemsSource
        {
            get { return _itemsSource; }
            set { SetItemsSource(value, true); }
        }

        public virtual object GetRawItem(int position)
        {
            if (position < 0)
                return null;
            return ItemsSource?.ElementAtIndex(position);
        }

        public virtual int GetPosition(object value)
        {
            return ItemsSource.IndexOf(value);
        }

        public Filter Filter
        {
            get
            {
                if (_filter == null)
                    _filter = new EmptyFilter(this);
                return _filter;
            }
            set { _filter = value; }
        }

        #endregion

        #region Overrides of BaseAdapter

        public override int GetItemViewType(int position)
        {
            if (ItemsSource == null)
                return Adapter.IgnoreItemViewType;
            var selector = _itemTemplateProvider.GetDataTemplateSelector() as IResourceDataTemplateSelector;
            if (selector == null)
                return 0;
            if (_resourceTypeToItemType == null)
                _resourceTypeToItemType = new Dictionary<int, int>();
            var id = selector.SelectTemplate(GetRawItem(position), _container);
            int type;
            if (!_resourceTypeToItemType.TryGetValue(id, out type))
            {
                type = _currentTypeIndex++;
                _resourceTypeToItemType[id] = type;
            }
            return type;
        }

        public override bool HasStableIds => _stableIdProvider != null;

        public override int ViewTypeCount
        {
            get
            {
                var resourceDataTemplateSelector = _itemTemplateProvider.GetDataTemplateSelector() as IResourceDataTemplateSelector;
                if (resourceDataTemplateSelector == null)
                    return 1;
                return resourceDataTemplateSelector.TemplateTypeCount;
            }
        }

        public override int Count
        {
            get
            {
                if (ItemsSource == null)
                    return 0;
                return ItemsSource.Count();
            }
        }

        public override View GetDropDownView(int position, View convertView, ViewGroup parent)
        {
            return GetViewInternal(position, convertView, parent, _dropDownTemplateProvider, _defaultDropDownTemplate);
        }

        public override Object GetItem(int position)
        {
            return null;
        }

        public override long GetItemId(int position)
        {
            if (_stableIdProvider == null)
                return position;
            return _stableIdProvider.GetItemId(GetRawItem(position));
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            return GetViewInternal(position, convertView, parent, _itemTemplateProvider, global::Android.Resource.Layout.SimpleListItem1);
        }

        #endregion

        #region Methods

        protected virtual void SetItemsSource(IEnumerable value, bool notifyDataSet)
        {
            if (ReferenceEquals(value, _itemsSource) || !this.IsAlive())
                return;
            if (_weakHandler == null)
            {
                _itemsSource = value;
                if (notifyDataSet)
                    NotifyDataSetChanged();
            }
            else
            {
                var notifyCollectionChanged = _itemsSource as INotifyCollectionChanged;
                if (notifyCollectionChanged != null)
                    notifyCollectionChanged.CollectionChanged -= _weakHandler;
                _itemsSource = value;
                if (notifyDataSet)
                    NotifyDataSetChanged();
                notifyCollectionChanged = _itemsSource as INotifyCollectionChanged;
                if (notifyCollectionChanged != null)
                    notifyCollectionChanged.CollectionChanged += _weakHandler;
            }
        }

        protected virtual View CreateView(object value, View convertView, ViewGroup parent, DataTemplateProvider templateProvider, int defaultTemplate, out LayoutInflaterResult inflaterResult)
        {
            inflaterResult = null;
            var valueView = value as View;
            if (valueView != null)
                return valueView;

            int? templateId = null;
            int id;
            if (templateProvider.TrySelectResourceTemplate(value, out id))
                templateId = id;
            else
            {
                object template;
                if (templateProvider.TrySelectTemplate(value, convertView, out template))
                {
                    if (template != null)
                    {
                        if (ReferenceEquals(convertView, template))
                            return convertView;

                        valueView = template as View;
                        if (valueView != null)
                        {
                            valueView.SetDataContext(value);
                            return valueView;
                        }
                        if (template is int)
                            templateId = (int)template;
                        else
                            value = template;
                    }
                }
                else
                    templateId = templateProvider.GetTemplateId();
            }

            if (templateId == null)
            {
                if (!(convertView is TextView))
                    convertView = LayoutInflater.Inflate(defaultTemplate, null);
                var textView = convertView as TextView;
                if (textView != null)
                    textView.Text = value.ToStringSafe("(null)");
                return textView;
            }
            var oldId = GetViewTemplateId(convertView);
            if (oldId == null || oldId.Value != templateId.Value)
                convertView = CreateView(value, parent, templateId.Value, out inflaterResult);
            convertView.SetDataContext(value);
            return convertView;
        }

        protected virtual View CreateView(object value, ViewGroup parent, int templateId, out LayoutInflaterResult inflaterResult)
        {
            inflaterResult = LayoutInflater.InflateEx(templateId, parent, false);
            inflaterResult.View.SetTag(Resource.Id.ListTemplateId, templateId);
            return inflaterResult.View;
        }

        protected virtual void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            var item = Container as Object;
            if (!item.IsAlive() || !this.IsAlive())
            {
                SetItemsSource(null, false);
                return;
            }
            var adapterView = Container as AdapterView;
            if (adapterView != null && args.Action != NotifyCollectionChangedAction.Add)
            {
                var value = adapterView.GetBindingMemberValue(AttachedMembers.AdapterView.SelectedItem);
                if (value != null && GetPosition(value) < 0)
                {
                    var index = args.OldStartingIndex;
                    var maxIndex = ItemsSource.Count() - 1;
                    while (index > maxIndex)
                        --index;
                    adapterView.SetBindingMemberValue(AttachedMembers.AdapterView.SelectedItem, GetRawItem(index));
                }
            }
            NotifyDataSetChanged();
        }

        protected virtual int? GetViewTemplateId([CanBeNull] View view)
        {
            var tag = view?.GetTag(Resource.Id.ListTemplateId);
            if (tag == null)
                return null;
            return (int)tag;
        }

        private View GetViewInternal(int position, View convertView, ViewGroup parent, DataTemplateProvider provider, int defaultTemplate)
        {
            if (ItemsSource == null)
                return null;
            LayoutInflaterResult result;
            var view = CreateView(GetRawItem(position), convertView, parent, provider, defaultTemplate, out result);
            if (view != null && !ReferenceEquals(view, convertView))
                view.SetBindingMemberValue(AttachedMembersBase.Object.Parent, Container);
            result?.ApplyBindings();
            return view;
        }

        private bool IsSpinner()
        {
            return Container is Spinner || AndroidToolkitExtensions.IsActionBar(Container);
        }

        private void ActivityViewOnDestroyed(Activity sender)
        {
            ((IActivityView)sender).Mediator.Destroyed -= _listener.Handle;
            SetItemsSource(null, false);
            var adapterView = _container as AdapterView;
            if (adapterView.IsAlive() && ReferenceEquals(AttachedMembersRegistration.GetAdapter(adapterView), this))
                AttachedMembersRegistration.SetAdapter(adapterView, null);
        }

        #endregion        
    }
}
