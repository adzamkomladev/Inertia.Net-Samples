using ReactSqliteBlog.Data;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Tests;

public sealed class SocialPersistenceTests(BlogFactory factory) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Relationships_are_unique_archives_are_filtered_and_deletion_cascades()
    {
        var owner = await factory.CreateUserAsync(BlogFactory.NewEmail("social-owner"));
        var reader = await factory.CreateUserAsync(BlogFactory.NewEmail("social-reader"));
        factory.WithDb(db =>
        {
            var post = new Post { AuthorId = owner, Title = "Social persistence", Body = "test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Posts.Add(post);
            db.SaveChanges();
            db.Bookmarks.Add(new() { UserId = reader, PostId = post.Id });
            db.Reposts.Add(new() { UserId = reader, PostId = post.Id });
            db.SeenPosts.Add(new() { UserId = reader, PostId = post.Id, FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            db.SaveChanges();
            db.ChangeTracker.Clear();
            db.Bookmarks.Add(new() { UserId = reader, PostId = post.Id });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();
            db.Follows.Add(new() { FollowerId = reader, FolloweeId = reader });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();
            db.Posts.Where(p => p.Id == post.Id).ExecuteUpdate(s => s.SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
            Assert.False(db.Posts.Any(p => p.Id == post.Id));
            Assert.True(db.Posts.IgnoreQueryFilters().Any(p => p.Id == post.Id && p.AuthorId == owner));
            Assert.True(db.Bookmarks.Any(b => b.PostId == post.Id));
            db.Posts.IgnoreQueryFilters().Where(p => p.Id == post.Id).ExecuteDelete();
            Assert.False(db.Bookmarks.Any(b => b.PostId == post.Id));
            Assert.False(db.Reposts.Any(b => b.PostId == post.Id));
            Assert.False(db.SeenPosts.Any(b => b.PostId == post.Id));
            return true;
        });
    }
}
