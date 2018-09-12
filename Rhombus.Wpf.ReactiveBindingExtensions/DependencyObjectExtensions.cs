using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using JetBrains.Annotations;

namespace Rhombus.Wpf.ReactiveBindingExtensions {
    internal static class DependencyObjectExtensions {
        public static IObservable<TProperty> Observe<TProperty>(this DependencyObject component, DependencyProperty dependencyProperty) {
            return Observable.Create<TProperty>(observer => {
                void Update(object sender, EventArgs args) {
                    observer.OnNext((TProperty) ((DependencyObject) sender).GetValue(dependencyProperty));
                }
                var property = DependencyPropertyDescriptor.FromProperty(dependencyProperty, component.GetType());
                property.AddValueChanged(component, Update);
                
                return delegate {
                    property.RemoveValueChanged(component, Update);
                };
            });
    }
    }
}