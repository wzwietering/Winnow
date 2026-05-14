using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to
/// <see cref="WinnowOptions"/> or <see cref="GraphOptionsBase"/>.
/// </summary>
/// <remarks>
/// Each method mutates the supplied options instance in place and also returns
/// it so the call can be embedded in a property initialiser. The return type is
/// the base options type — when you need to keep working with a derived options
/// type (e.g. <see cref="InsertOptions"/>), assign the options to a variable
/// first and call this method on the variable.
/// </remarks>
public static class ValidationOptionsExtensions
{
    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline to a parent-only
    /// (non-graph) options object. The delegate is invoked once per entity in
    /// each batch; emit <see cref="ValidationError"/> instances via the collector
    /// to reject the entity. Calling this method more than once replaces the
    /// previously configured validator — the last call wins.
    /// </summary>
    public static WinnowOptions WithValidation<TEntity>(
        this WinnowOptions options,
        ValidatorDelegate<TEntity> validator)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        options.Validation = new ValidationOptions(typeof(TEntity), validator);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline to a graph options object.
    /// </summary>
    public static GraphOptionsBase WithValidation<TEntity>(
        this GraphOptionsBase options,
        ValidatorDelegate<TEntity> validator)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        options.Validation = new ValidationOptions(typeof(TEntity), validator);
        return options;
    }

    /// <summary>
    /// Attaches a pre-validation pipeline driven by
    /// <c>System.ComponentModel.DataAnnotations</c> attributes declared on
    /// <typeparamref name="TEntity"/>'s public instance properties.
    /// Each attribute discovered is cached per type, so the reflection cost is
    /// paid once and amortised across all subsequent batches.
    /// </summary>
    public static WinnowOptions WithDataAnnotations<TEntity>(this WinnowOptions options)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        options.Validation = new ValidationOptions(typeof(TEntity), validator);
        return options;
    }

    /// <summary>
    /// Attaches a DataAnnotations-driven pre-validation pipeline to a graph options object.
    /// </summary>
    public static GraphOptionsBase WithDataAnnotations<TEntity>(this GraphOptionsBase options)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        options.Validation = new ValidationOptions(typeof(TEntity), validator);
        return options;
    }
}
