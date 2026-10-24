using Microsoft.AspNetCore.HttpOverrides;

namespace TechLandier.Infrastructure;

public static class ProxySetup
{
    /// <summary>Trust forwarded headers only from the private Docker/proxy networks.</summary>
    public static ForwardedHeadersOptions TrustedProxyOptions(IConfiguration config)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var cidr in config["App:TrustedProxyNetworks"] is { Length: > 0 } nets
                     ? nets.Split(',', StringSplitOptions.TrimEntries)
                     : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"])
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        return options;
    }
}
