namespace Sparta.Audit
{

    // Optional, DI-provided audit-only save observers (including integration fault injection).
    public interface IAuditSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor { }
}
