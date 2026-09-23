using DevExpress.Persistent.BaseImpl.EFCore.AuditTrail;

namespace Sparta.Audit
{
    public class SafeAuditFilter : IAuditFilterDataProvider
    {
        public bool NeedToSave(IAuditDataItemPersistent item)
        {
            return !(item.PropertyName?.Contains("Password", StringComparison.OrdinalIgnoreCase) == true || item.PropertyName?.Contains("Token", StringComparison.OrdinalIgnoreCase) == true);
        }
    }
}
