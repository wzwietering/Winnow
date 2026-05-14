using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to a flat
/// (non-graph) options object: <see cref="WinnowOptions"/>,
/// <see cref="InsertOptions"/>, <see cref="DeleteOptions"/>, or
/// <see cref="UpsertOptions"/>.
/// </summary>
/// <remarks>
/// Overloads are provided per concrete options type so the fluent chain keeps
/// the caller's exact derived type — <c>new InsertOptions().WithDataAnnotations&lt;Order&gt;()</c>
/// still returns <see cref="InsertOptions"/>, not the base.
/// </remarks>
public static class ValidationOptionsExtensions
{
    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity})"/>
    public static WinnowOptions WithValidation<TEntity>(this WinnowOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureFlat(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity})"/>
    public static InsertOptions WithValidation<TEntity>(this InsertOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureFlat(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity})"/>
    public static DeleteOptions WithValidation<TEntity>(this DeleteOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureFlat(options, validator);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline. The delegate is
    /// invoked once per entity in each batch; emit <see cref="ValidationError"/>
    /// instances via the collector to reject the entity. Calling this method
    /// more than once replaces the previously configured validator — the last
    /// call wins.
    /// </summary>
    public static UpsertOptions WithValidation<TEntity>(this UpsertOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureFlat(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions)"/>
    public static WinnowOptions WithDataAnnotations<TEntity>(this WinnowOptions options) where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions)"/>
    public static InsertOptions WithDataAnnotations<TEntity>(this InsertOptions options) where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions)"/>
    public static DeleteOptions WithDataAnnotations<TEntity>(this DeleteOptions options) where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options);
        return options;
    }

    /// <summary>
    /// Attaches a pre-validation pipeline driven by
    /// <c>System.ComponentModel.DataAnnotations</c> attributes declared on
    /// <typeparamref name="TEntity"/>'s public instance properties. Reflection
    /// cost is paid once per type and amortised across all subsequent batches.
    /// </summary>
    public static UpsertOptions WithDataAnnotations<TEntity>(this UpsertOptions options) where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options);
        return options;
    }

    private static void ConfigureFlat<TEntity>(WinnowOptions options, ValidatorDelegate<TEntity> validator)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        options.Validation = ValidationOptions.CreateFlat(typeof(TEntity), validator);
    }

    private static void ConfigureFlatDataAnnotations<TEntity>(WinnowOptions options)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        options.Validation = ValidationOptions.CreateFlat(typeof(TEntity), validator, isDataAnnotationsValidator: true);
    }
}

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to a graph options
/// object: <see cref="InsertGraphOptions"/>, <see cref="GraphOptions"/>,
/// <see cref="DeleteGraphOptions"/>, or <see cref="UpsertGraphOptions"/>.
/// </summary>
/// <remarks>
/// Overloads are provided per concrete options type so the fluent chain keeps
/// the caller's exact derived type. The resulting
/// <see cref="GraphValidationOptions"/> exposes
/// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking children.
/// </remarks>
public static class GraphValidationOptionsExtensions
{
    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity})"/>
    public static InsertGraphOptions WithValidation<TEntity>(this InsertGraphOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureGraph(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity})"/>
    public static GraphOptions WithValidation<TEntity>(this GraphOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureGraph(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity})"/>
    public static DeleteGraphOptions WithValidation<TEntity>(this DeleteGraphOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureGraph(options, validator);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline to a graph options
    /// object. The resulting <see cref="GraphValidationOptions"/> exposes
    /// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking
    /// child entities.
    /// </summary>
    public static UpsertGraphOptions WithValidation<TEntity>(this UpsertGraphOptions options, ValidatorDelegate<TEntity> validator) where TEntity : class
    {
        ConfigureGraph(options, validator);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions)"/>
    public static InsertGraphOptions WithDataAnnotations<TEntity>(this InsertGraphOptions options) where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions)"/>
    public static GraphOptions WithDataAnnotations<TEntity>(this GraphOptions options) where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions)"/>
    public static DeleteGraphOptions WithDataAnnotations<TEntity>(this DeleteGraphOptions options) where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options);
        return options;
    }

    /// <summary>
    /// Attaches a DataAnnotations-driven pre-validation pipeline to a graph
    /// options object. Set
    /// <see cref="GraphValidationOptions.IncludeNavigations"/> on the resulting
    /// <see cref="GraphOptionsBase.Validation"/> to descend into navigation
    /// properties and validate reachable children.
    /// </summary>
    public static UpsertGraphOptions WithDataAnnotations<TEntity>(this UpsertGraphOptions options) where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options);
        return options;
    }

    private static void ConfigureGraph<TEntity>(GraphOptionsBase options, ValidatorDelegate<TEntity> validator)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        options.Validation = new GraphValidationOptions(typeof(TEntity), validator);
    }

    private static void ConfigureGraphDataAnnotations<TEntity>(GraphOptionsBase options)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        options.Validation = new GraphValidationOptions(typeof(TEntity), validator, isDataAnnotationsValidator: true);
    }
}
