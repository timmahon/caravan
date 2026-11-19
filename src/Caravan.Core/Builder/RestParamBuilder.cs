using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>Configures a <see cref="RestParamDefinition"/>.</summary>
/// <param name="definition">The parameter.</param>
public sealed class RestParamBuilder(RestParamDefinition definition)
{
    /// <summary>The parameter being configured.</summary>
    public RestParamDefinition Definition { get; } = definition;

    /// <summary>Sets the description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder Description(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Sets whether the parameter is required (default true).</summary>
    /// <param name="required">Whether it is required.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder Required(bool required = true)
    {
        Definition.Required = required;
        return this;
    }

    /// <summary>Sets the default value (set as a header when a query parameter is missing).</summary>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder DefaultValue(string value)
    {
        Definition.DefaultValue = value;
        return this;
    }

    /// <summary>Sets the OpenAPI data type (<c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>...).</summary>
    /// <param name="dataType">The data type.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder DataType(string dataType)
    {
        Definition.DataType = dataType;
        return this;
    }

    /// <summary>Sets the OpenAPI format (<c>int64</c>, <c>date-time</c>...).</summary>
    /// <param name="dataFormat">The format.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder DataFormat(string dataFormat)
    {
        Definition.DataFormat = dataFormat;
        return this;
    }

    /// <summary>Sets the element type of an <c>array</c> parameter.</summary>
    /// <param name="arrayType">The element type.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder ArrayType(string arrayType)
    {
        Definition.ArrayType = arrayType;
        return this;
    }

    /// <summary>Restricts the values allowed.</summary>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public RestParamBuilder AllowableValues(params string[] values)
    {
        Definition.AllowableValues.AddRange(values);
        return this;
    }
}
