using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace VuePgReplicasAdvBlog.Infrastructure.Setup;

public static class ProxySetup
{
    public static ForwardedHeadersOptions TrustedProxyOptions(IConfiguration config)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        var networks = config["App:TrustedProxyNetworks"] is { Length: > 0 } value
            ? value.Split(',', StringSplitOptions.TrimEntries)
            : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
        foreach (var network in networks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        return options;
    }
}
