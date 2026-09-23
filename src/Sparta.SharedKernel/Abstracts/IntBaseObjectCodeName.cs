using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EFCore;
using DevExpress.ExpressApp.DataLocking;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.Validation;
using DevExpress.ExpressApp.DC;

namespace Sparta.SharedKernel.Interfaces;

public abstract class IntBaseObjectCodeName : IXafEntityObject, IObjectSpaceLink, IDeferredDeletion, IEFCoreBaseObject, IOptimisticLock, IHasCode, IHasName, IHasRemark
{
    protected IObjectSpace ObjectSpace = null!;
    
    public IntBaseObjectCodeName()
    {
        if (this is INotifyPropertyChanged notifyPropertyChanged)
        {
            notifyPropertyChanged.PropertyChanged += OnPropertyChanged;
        }
    }

    #region Implementation for Event Handlers and Methods 
    IObjectSpace IObjectSpaceLink.ObjectSpace
    {
        get => ObjectSpace;
        set => ObjectSpace = value;
    }

    //
    // Summary:
    //     Partially implements the DevExpress.ExpressApp.IXafEntityObject interface in
    //     the DevExpress.Persistent.BaseImpl.EF.BaseObject class.
    public virtual void OnCreated()
    {
        //Remarks = ObjectSpace.CreateObject<Remarks>();
    }

    //
    // Summary:
    //     Partially implements the DevExpress.ExpressApp.IXafEntityObject interface in
    //     the DevExpress.Persistent.BaseImpl.EF.BaseObject class.
    public virtual void OnSaving()
    {
        //if (ObjectSpace != null && Remarks == null)
        //{
        //    Remarks = ObjectSpace.CreateObject<Remarks>();
        //}
    }

    //
    // Summary:
    //     Partially implements the DevExpress.ExpressApp.IXafEntityObject interface in
    //     the DevExpress.Persistent.BaseImpl.EF.BaseObject class.
    public virtual void OnLoaded()
    {

    }

    /// <summary>
    /// Returns a string that represents the current object, using the value of the declared default member if
    /// available.
    /// </summary>
    /// <remarks>This override attempts to provide a more meaningful string representation by using the value
    /// of the declared default member, if one is defined for the object's type. If the default member is not available
    /// or its value is null, the base implementation is used.</remarks>
    /// <returns>A string representation of the current object. If a declared default member exists and its value is not null,
    /// returns the string representation of that value; otherwise, returns the result of the base implementation.</returns>
    public override string? ToString()
    {
        if (ObjectSpace == null || !ObjectSpace.IsDisposed)
        {
            ITypesInfo typesInfo = ObjectSpace?.TypesInfo ?? XafTypesInfo.Instance;
            IMemberInfo declaredDefaultMember = typesInfo.FindTypeInfo(GetType()).DeclaredDefaultMember;
            if (declaredDefaultMember != null)
            {
                try
                {
                    object value = declaredDefaultMember.GetValue(this);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
                catch
                {

                }
            }
        }

        return base.ToString();
    }

    protected virtual void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {

    }

    //
    // Summary:
    //     Sets the object�s property value bypassing security checks.
    //
    // Parameters:
    //   propertyName:
    //     The name of the property to set.
    //
    //   value:
    //     The value to assign to the property.
    //
    // Type parameters:
    //   T:
    //     The type of the value assigned to the property.
    protected void SetPropertyValueWithSecurityBypass<T>(string propertyName, T value)
    {
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, propertyName, value);
    }

    protected T EvaluateAlias<T>([CallerMemberName] string? propertyName = null)
    {
        return new CalculatedPropertyEvaluator().Evaluate<T>(this, propertyName);
    }

    protected object EvaluateAlias([CallerMemberName] string? propertyName = null)
    {
        return new CalculatedPropertyEvaluator().Evaluate(this, propertyName);
    }
    #endregion

    #region Implementation for Object DevExpressApp Framework                
    //
    // Summary:
    //     The key property for the DevExpress.Persistent.BaseImpl.EF.BaseObject class.
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Browsable(false)]
    [VisibleInListView(false)]
    [VisibleInDetailView(false)]
    [VisibleInLookupListView(false)]
    public virtual int ID { get; set; }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [ConcurrencyCheck]
    [NotMapped]
    [UseInAuditTrail(false)]
    public virtual int OptimisticLockField { get; set; }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [NotMapped]
    public virtual int GCRecord { get; set; }
    #endregion

    #region Impelement Business Properties for IHasCode, IHasName
    private string code = string.Empty;    
    
    
    [MaxLength(10)]
    [RuleRequiredField(DefaultContexts.Save, SkipNullOrEmptyValues = false)]    
    public virtual string Code
    {
        get => code;
        set
        {
            if (code == value) return;
            code = value?.Trim() ?? string.Empty;
        }
    }

    private string name = string.Empty;
            
    [RuleRequiredField(DefaultContexts.Save)]
    [MaxLength(150)]
    public virtual string Name
    {
        get => name;
        set
        {
            if (name == value) return;
            name = value?.Trim() ?? string.Empty;
        }
    }
        
    ////[FullTextSearch, AllowFilter, AllowSort]
    ////[SearchMemberOptions(SearchMemberMode.Exclude)]
    //[PersistentAlias("Concat(Code, ' - ', Name)")]
    ////[ModelDefault("AllowSorting", "False")]
    ////[ModelDefault("AllowGrouping", "False")]
    //public string Description
    //{
    //    get
    //    {
    //        //return string.Join("; ",this.Code , this.name) ;
    //        return EvaluateAlias<string>();
    //    }
    //}

    string? remark;            
    public virtual string? Remark
    {
        get
        {
            return remark;
        }
        set
        {
            if (remark == value)
            {
                return;
            }
            remark = value;
        }
    }

    #endregion
}

