using Microsoft.EntityFrameworkCore;

namespace Sparta.SharedKernel
{
    public static class ModelConfiguration
    {
        public static void ConfigureBusinessModel(this ModelBuilder modelBuilder)
        {
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
            modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime)))
                {
                    property.SetValueConverter
                    (
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
                    );
                }                    
            }
                
        }
    }
}
