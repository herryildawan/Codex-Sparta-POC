using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.WebApi.Services;
using DevExpress.Persistent.Validation;

namespace Sparta.WebApi;

/// <summary>
/// Runs XAF validation rules for objects changed through the standard OData endpoints.
/// XAF intentionally does not enable this for Web API CRUD endpoints by default.
/// </summary>
public sealed class ValidatedDataService : DataService
{
    private readonly IValidator validator;

    public ValidatedDataService(
        IObjectSpaceFactory objectSpaceFactory,
        ITypesInfo typesInfo,
        IObjectDeltaHandler objectDeltaHandler,
        IValidator validator)
        : base(objectSpaceFactory, typesInfo, objectDeltaHandler)
    {
        this.validator = validator;
    }

    protected override IObjectSpace CreateObjectSpace(Type objectType)
    {
        var objectSpace = base.CreateObjectSpace(objectType);
        objectSpace.Committing += ValidateModifiedObjects;
        return objectSpace;
    }

    private void ValidateModifiedObjects(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var objectSpace = (IObjectSpace)sender!;
        objectSpace.ValidateModifiedObjects(validator);
    }
}

public static class ObjectSpaceValidationExtensions
{
    public static void ValidateModifiedObjects(this IObjectSpace objectSpace, IValidator validator)
    {
        var result = validator.RuleSet.ValidateAllTargets(
            objectSpace,
            objectSpace.ModifiedObjects,
            DefaultContexts.Save);

        if (result.ValidationOutcome == ValidationOutcome.Error)
            throw new DevExpress.Persistent.Validation.ValidationException(result);
    }

    public static void ValidateAndCommit(this IObjectSpace objectSpace, IValidator validator)
    {
        objectSpace.ValidateModifiedObjects(validator);
        objectSpace.CommitChanges();
    }
}
