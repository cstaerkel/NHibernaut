using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.App.ViewModels;
using NHibernaut.Client;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class ConnectionViewModelTests
{
    private sealed class RecordingFactory : IDashboardClientFactory
    {
        public DashboardConnection? Last;
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default)
        { Last = c; return Task.FromResult<IDashboardClient>(new FakeDashboardClient()); }
    }

    /// <summary>Records "create" into a shared order list each time it builds a client.</summary>
    private sealed class OrderRecordingFactory : IDashboardClientFactory
    {
        private readonly List<string> _order;
        public OrderRecordingFactory(List<string> order) => _order = order;
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default)
        { _order.Add("create"); return Task.FromResult<IDashboardClient>(new FakeDashboardClient()); }
    }

    private sealed class ThrowingFactory : IDashboardClientFactory
    {
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated failure");
    }

    private sealed class AccessDeniedFactory : IDashboardClientFactory
    {
        private readonly Exception _ex;
        public AccessDeniedFactory(Exception ex) => _ex = ex;
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default) =>
            throw _ex;
    }

    [Fact]
    public async Task Remote_connect_builds_remote_connection()
    {
        var factory = new RecordingFactory();
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = false,
            Url = "http://h:1",
            Token = "t"
        };

        IDashboardClient? receivedClient = null;
        vm.Connected += client => { receivedClient = client; return Task.CompletedTask; };

        await vm.ConnectAsync();

        Assert.NotNull(factory.Last);
        Assert.Equal(DashboardMode.Remote, factory.Last!.Mode);
        Assert.Equal("http://h:1", factory.Last.Url);
        Assert.Equal("t", factory.Last.Token);
        Assert.Equal("connected", vm.Status);
        Assert.NotNull(receivedClient);
    }

    [Fact]
    public async Task Embedded_connect_builds_embedded_connection()
    {
        var factory = new RecordingFactory();
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = true,
            BindAddress = "0.0.0.0",
            Port = 6000
        };

        await vm.ConnectAsync();

        Assert.NotNull(factory.Last);
        Assert.Equal(DashboardMode.Embedded, factory.Last!.Mode);
        Assert.Equal("0.0.0.0", factory.Last.BindAddress);
        Assert.Equal(6000, factory.Last.Port);
    }

    [Fact]
    public async Task Blank_token_becomes_null()
    {
        var factory = new RecordingFactory();
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = false,
            Url = "http://host:5005",
            Token = "   "
        };

        await vm.ConnectAsync();

        Assert.NotNull(factory.Last);
        Assert.Null(factory.Last!.Token);
    }

    [Fact]
    public async Task Preset_overrides_fields()
    {
        var factory = new RecordingFactory();
        var vm = new ConnectionViewModel(factory);

        await vm.ConnectAsync(DashboardConnection.Embedded(port: 5006));

        Assert.NotNull(factory.Last);
        Assert.Equal(5006, factory.Last!.Port);
        Assert.True(vm.Embedded);
    }

    [Fact]
    public async Task Connect_failure_sets_error_status()
    {
        var factory = new ThrowingFactory();
        var vm = new ConnectionViewModel(factory);

        var exception = await Record.ExceptionAsync(() => vm.ConnectAsync());

        Assert.Null(exception); // no exception should escape
        Assert.StartsWith("error:", vm.Status);
    }

    [Fact]
    public async Task Embedded_nonloopback_access_denied_sets_urlacl_remediation_status()
    {
        // Simulate HTTP.sys rejecting http://+:<port>/ on a non-loopback embedded bind.
        // Use a message that does NOT contain "Access is denied" so this can only match via ErrorCode == 5
        // (the message substring is localized on non-English Windows; the ErrorCode path must work regardless).
        var ex = new System.Net.HttpListenerException(5, "Zugriff verweigert");
        var factory = new AccessDeniedFactory(ex);
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = true,
            BindAddress = "0.0.0.0",
            Port = 7777,
            Token = "secret",
        };

        await vm.ConnectAsync();

        Assert.Contains("netsh http add urlacl", vm.Status);
        Assert.Contains("7777", vm.Status);
    }

    [Fact]
    public async Task Embedded_nonloopback_access_denied_via_message_sets_remediation_status()
    {
        // Some hosts surface the denial as a plain message rather than HttpListenerException.
        var ex = new InvalidOperationException("HttpListener: Access is denied.");
        var factory = new AccessDeniedFactory(ex);
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = true,
            BindAddress = "192.168.1.50",
            Port = 8080,
            Token = "secret",
        };

        await vm.ConnectAsync();

        Assert.Contains("netsh http add urlacl", vm.Status);
    }

    [Fact]
    public async Task Embedded_loopback_access_denied_keeps_generic_error()
    {
        // Loopback bind never registers http://+ — so even an access-denied error stays generic.
        var ex = new System.Net.HttpListenerException(5, "Access is denied");
        var factory = new AccessDeniedFactory(ex);
        var vm = new ConnectionViewModel(factory)
        {
            Embedded = true,
            BindAddress = "127.0.0.1",
            Port = 5005,
        };

        await vm.ConnectAsync();

        Assert.StartsWith("error:", vm.Status);
        Assert.DoesNotContain("netsh", vm.Status);
    }

    [Fact]
    public async Task Reconnect_disposes_old_client_before_creating_new_one()
    {
        // Pins the dispose-before-create ordering that prevents an embedded reconnect from
        // tearing down the freshly-started server (the new client's StartAsync must run AFTER
        // the old client/server has been disposed).
        var order = new List<string>();
        var factory = new OrderRecordingFactory(order);
        var vm = new ConnectionViewModel(factory);
        vm.Disconnecting += () => { order.Add("dispose"); return Task.CompletedTask; };

        // Initial connect: nothing to dispose yet, but the hook still fires before create.
        await vm.ConnectAsync();
        // Second connect: the old client must be disposed BEFORE the new one is created.
        await vm.ConnectAsync();

        // Four events total; the last two pin the critical ordering on the second connect.
        Assert.Equal(new[] { "dispose", "create", "dispose", "create" }, order);
        Assert.Equal("connected", vm.Status);
    }
}
