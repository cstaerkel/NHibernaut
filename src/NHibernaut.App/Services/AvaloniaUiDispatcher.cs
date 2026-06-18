using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace NHibernaut.App.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}
