using System;
using System.Threading.Tasks;

namespace NHibernaut.App.Services;

public interface IUiDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Func<Task> action);
}
