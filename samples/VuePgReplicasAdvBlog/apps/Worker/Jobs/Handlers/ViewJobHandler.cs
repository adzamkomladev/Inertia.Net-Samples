using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Analytics;

namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed class RecordViewHandler(AppDb db, RealtimeEvents realtime) : IJobHandler<RecordView>
{
    public async Task HandleAsync(RecordView view, CancellationToken ct)
    {
        // A retry uses the same view ID; deleting a post also makes its queued beacon a no-op.
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "PostViews" ("Id", "PostId", "ViewerHash", "Referrer", "At")
            SELECT {view.Id}, "Id", {view.ViewerHash}, {view.Referrer}, {view.At}
            FROM "Posts" WHERE "Id" = {view.PostId}
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
        if (inserted > 0) await realtime.ChangedAsync(view.PostId);
    }
}
