extern alias main;

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Tests;

public class LibraryBoundaryTests
{
    static readonly string[] Features = ["Auth", "Posts", "Comments", "Likes", "Uploads", "Social", "Preferences", "Analytics", "Discovery", "Digests", "Email", "Search"];

    [Fact]
    public void Common_has_no_feature_dependencies()
    {
        Assert.DoesNotContain(typeof(AppDb).Assembly.GetReferencedAssemblies(), reference => Features.Contains(reference.Name));
    }

    [Fact]
    public void Libraries_have_no_Inertia_dependency()
    {
        foreach (var name in Features.Concat(["Common", "ServiceDefaults"]))
            Assert.DoesNotContain(Assembly.Load(name).GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Inertia", StringComparison.Ordinal));
        Assert.Contains(typeof(main::Program).Assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Inertia", StringComparison.Ordinal));
    }

    [Fact]
    public void Features_have_no_HTTP_response_request_or_cookie_dependency()
    {
        foreach (var name in Features)
            Assert.DoesNotContain(Assembly.Load(name).GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("Microsoft.AspNetCore.Http", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore.Authentication.Cookies", StringComparison.Ordinal));
    }

    [Fact]
    public void Libraries_do_not_map_endpoints_or_configure_an_application()
    {
        foreach (var name in Features.Concat(["Common", "ServiceDefaults"]))
        {
            var assembly = Assembly.Load(name);
            var methods = assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
            Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter =>
                typeof(IEndpointRouteBuilder).IsAssignableFrom(parameter.ParameterType)
                || parameter.ParameterType == typeof(WebApplicationBuilder)
                || parameter.ParameterType == typeof(WebApplication)));
        }
    }

    [Fact]
    public void Feature_endpoints_are_owned_by_MainApp()
    {
        var methods = typeof(main::Program).Assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static));
        foreach (var name in new[] { "MapAuth", "MapPosts", "MapComments", "MapLikes", "MapUploads", "MapSocial", "MapPreferences", "MapAnalytics", "MapDigests" })
            Assert.Contains(methods, method => method.Name == name && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(IEndpointRouteBuilder)));
    }
}
