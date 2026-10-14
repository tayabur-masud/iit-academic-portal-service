using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// Stamps created and last-modified (who and when) on every <see cref="AuditedEntity"/> as it is saved, with the server clock and
/// the account of the current request. It applies to every save from every context, so no feature has to remember it and a client
/// can never supply the values. Outside a request (a background job) the account is null.
/// </summary>
public sealed class AuditedEntityInterceptor(TimeProvider clock, IAuditRequestContext request) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var actor = request.ActorUserId;
        foreach (var entry in context.ChangeTracker.Entries<AuditedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.StampCreated(now, actor);
                    break;
                case EntityState.Modified:
                    entry.Entity.StampModified(now, actor);
                    break;
            }
        }
    }
}
