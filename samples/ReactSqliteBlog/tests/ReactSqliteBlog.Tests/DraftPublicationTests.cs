using System.Net;
using ReactSqliteBlog.Data;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Tests;

public class DraftPublicationTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Draft_publication_sets_the_first_date_and_archive_preserves_it()
    {
        var email = BlogFactory.NewEmail("draft");
        var uid = await f.CreateUserAsync(email);
        var owner = f.NewClient(); await owner.LoginOkAsync(email);
        var reader = f.NewClient();
        using var create = await owner.PostAsync("/posts", new {title="Private draft",body="draft body",isPublished=false});
        Assert.Equal(HttpStatusCode.Found,create.StatusCode);
        var id = Guid.Parse(create.Headers.Location!.OriginalString.Split('/').Last());
        var old = new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        f.WithDb(db => { var p=db.Posts.IgnoreQueryFilters().Single(p=>p.Id==id); p.PublishedAt=old; return db.SaveChanges(); });
        using(var hidden=await reader.GetAsync($"/posts/{id}")) Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        using(var mine=await owner.GetAsync("/me/posts")) await mine.AssertInertiaAsync(p=>p.Has("posts.data",1));
        using(var edit=await owner.GetAsync($"/posts/{id}/edit")) await edit.AssertInertiaAsync(p=>p.Where("post.isPublished",false));
        using(var omitted=await owner.SendAsync(HttpMethod.Put,$"/posts/{id}",new {title="Still private",body="edited"})) Assert.Equal(HttpStatusCode.SeeOther,omitted.StatusCode);
        Assert.False(f.WithDb(db=>db.Posts.IgnoreQueryFilters().Single(p=>p.Id==id).IsPublished));
        Assert.Equal(old,f.WithDb(db=>db.Posts.IgnoreQueryFilters().Single(p=>p.Id==id).PublishedAt));
        var before=DateTime.UtcNow;
        using(var publish=await owner.SendAsync(HttpMethod.Put,$"/posts/{id}",new {title="Now published",body="public body",isPublished=true})) Assert.Equal(HttpStatusCode.SeeOther,publish.StatusCode);
        var published=f.WithDb(db=>db.Posts.Single(p=>p.Id==id).PublishedAt);
        Assert.InRange(published,before,DateTime.UtcNow);
        using(var visible=await reader.GetAsync($"/posts/{id}")) await visible.AssertInertiaAsync(p=>p.Where("post.isPublished",true));
        using(var edit=await owner.SendAsync(HttpMethod.Put,$"/posts/{id}",new {title="Published edit",body="still public",isPublished=true})) Assert.Equal(HttpStatusCode.SeeOther,edit.StatusCode);
        using(var reject=await owner.SendAsync(HttpMethod.Put,$"/posts/{id}",new {title="Not a draft",body="public",isPublished=false})) Assert.Equal(HttpStatusCode.SeeOther,reject.StatusCode);
        Assert.True(f.WithDb(db=>db.Posts.Single(p=>p.Id==id).IsPublished));
        using(var archive=await owner.SendAsync(HttpMethod.Put,$"/posts/{id}/archive")) Assert.Equal(HttpStatusCode.SeeOther,archive.StatusCode);
        using(var restore=await owner.SendAsync(HttpMethod.Delete,$"/posts/{id}/archive")) Assert.Equal(HttpStatusCode.SeeOther,restore.StatusCode);
        Assert.Equal(published,f.WithDb(db=>db.Posts.Single(p=>p.Id==id).PublishedAt));
    }
}
