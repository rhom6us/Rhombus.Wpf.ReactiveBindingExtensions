using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using JetBrains.Annotations;

namespace Rhombus.Wpf.ReactiveBindingExtensions {
    [MarkupExtensionReturnType(typeof(object))]
    internal class BindExtension : MarkupExtension {
        public BindExtension() { }

        public BindExtension(PropertyPath path) : this() {
            this.Path = path;
        }

        [ConstructorArgument("path")]
        public PropertyPath Path { get; set; }

        public BindingMode Mode { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) {

            if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget valueProvider) {
                _bindingTarget = valueProvider.TargetObject as DependencyObject;
                _bindingProperty = valueProvider.TargetProperty as DependencyProperty;

                //DataContext, TODO other sources
                var dataContextSourceElement = BindExtension.FindDataContextSource(_bindingTarget);
                dataContextSourceElement.DataContextChanged += this.DataContextSource_DataContextChanged;
                
                this.SetupBinding(dataContextSourceElement.DataContext);
            }

            //return default
            return _bindingProperty.DefaultMetadata.DefaultValue;
        }

        private void DataContextSource_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            this.RemoveBinding();
            if (e.NewValue != null) {
                this.SetupBinding(e.NewValue);
            }
        }

        private void SetupBinding(object source) {
            //sanity check
            if (source == null) {
                return;
            }

            var boundObject = BindExtension.GetObjectFromPath(source, this.Path.Path);
            if (boundObject == null) {
                return;
            }

            //set default BindingMode 
            if (this.Mode == BindingMode.Default) {
                if (_bindingProperty.GetMetadata(_bindingTarget) is FrameworkPropertyMetadata metadata && metadata.BindsTwoWayByDefault) {
                    this.Mode = BindingMode.TwoWay;
                }
                else {
                    this.Mode = BindingMode.OneWay;
                }
            }

            //bind to Observable and update property
            if (this.Mode == BindingMode.OneTime || this.Mode == BindingMode.OneWay || this.Mode == BindingMode.TwoWay) {
                this.SetupListenerBinding(boundObject);
            }

            //send property values to Observer
            if (this.Mode == BindingMode.OneWayToSource || this.Mode == BindingMode.TwoWay) {
                this.SetupEmitBinding(boundObject);
            }
        }

        private void RemoveBinding() {
            //stop listening to observable
            _listenSubscription?.Dispose();
            _emitSubscription?.Dispose();
        }

        private DependencyProperty _bindingProperty;
        private DependencyObject _bindingTarget;
        private IDisposable _emitSubscription;
        private IDisposable _listenSubscription;

        #region Listen

        private void SetupListenerBinding(object observable) {
            //IObservable<T> --> typeof(T)
            var observableGenericType = observable.GetType().GetInterfaces().Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IObservable<>)).GetGenericArguments()[0];

            //add subscription
            var method = typeof(BindExtension).GetMethod(nameof(BindExtension.SubscribePropertyForObservable), BindingFlags.NonPublic | BindingFlags.Instance);
            var generic = method.MakeGenericMethod(observableGenericType);
            generic.Invoke(this, new[] { observable, _bindingTarget, _bindingProperty });
        }

        private void SubscribePropertyForObservable<TProperty>(IObservable<TProperty> observable, DependencyObject d, DependencyProperty property) {
            if (observable == null) {
                return;
            }

            if (this.Mode == BindingMode.OneTime) {
                observable = observable.Take(1);
            }

            //automatic ToString
            if (property.PropertyType == typeof(string) && typeof(TProperty) != typeof(string)) {
                _listenSubscription = observable.Select(val => val.ToString()).ObserveOn(SynchronizationContext.Current).Subscribe(val => d.SetValue(property, val));
            }
            //any other case
            else {
                _listenSubscription = observable.ObserveOn(SynchronizationContext.Current).Subscribe(val => d.SetValue(property, val));
            }
        }

        #endregion

        #region Emit

        private void SetupEmitBinding(object observer) {
            _emitSubscription = SubScribeObserverForProperty(observer, _bindingTarget, _bindingProperty);
            
        }

        private static IDisposable SubScribeObserverForProperty(object observer, DependencyObject d, DependencyProperty propertyToMonitor) {
            return (IDisposable)_subScribeObserverForPropertyMethodInfo.MakeGenericMethod(propertyToMonitor.PropertyType).Invoke(null, new[] {observer, d, propertyToMonitor});
        }

        private static readonly MethodInfo _subScribeObserverForPropertyMethodInfo = typeof(BindExtension).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Where(p => p.Name == nameof(BindExtension.SubScribeObserverForProperty)).SingleOrDefault(p => p.IsGenericMethod) ?? throw new InvalidOperationException("method is missing");
        private static IDisposable SubScribeObserverForProperty<TProperty>(IObserver<TProperty> observer, DependencyObject d, DependencyProperty propertyToMonitor) {
            if (!propertyToMonitor.OwnerType.IsInstanceOfType(d) || observer == null)
                return null;
            
            return  d.Observe<TProperty>(propertyToMonitor).Subscribe(observer);

        }

        #endregion

        #region Helper

        /// <summary>
        ///     Not all DependencyObjects have a DataContext Property (e.g. RotateTransform). If the Binding happens there, this
        ///     method will travel up the logical tree to retrieve the first parent that does have it.
        /// </summary>
        private static FrameworkElement FindDataContextSource(DependencyObject d) {

            for (; !(d is FrameworkElement || d is null); d = LogicalTreeHelper.GetParent(d)) ;
            
            return (FrameworkElement) d;
        }

        /// <summary>
        ///     If the Binding looks something like this
        ///     {o:Bind Parent.Child.SubChild}, we need to retrieve SubChild
        /// </summary>
        /// <returns>The value returned by the path, null if any properties in the chain is null</returns>
        private static object GetObjectFromPath(object dataContext, [NotNull] string path) {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            return path.Split('.').Aggregate(dataContext, (current, prop) => current?.GetType().GetProperty(prop)?.GetValue(current));
        }

        #endregion
    }
}