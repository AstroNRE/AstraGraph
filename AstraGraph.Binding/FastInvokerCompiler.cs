using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using AstraGraph.Core;

namespace AstraGraph.Binding;

/// <summary>
/// Compiles MethodInfo into ultra-fast pre-bound delegates (Func&lt;AstraValue[], AstraValue&gt;)
/// completely eliminating MethodInfo.Invoke reflection overhead.
/// </summary>
public static class FastInvokerCompiler
{
    private static readonly MethodInfo AsBoolMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.AsBool))!;
    private static readonly MethodInfo AsInt32Method = typeof(AstraValue).GetMethod(nameof(AstraValue.AsInt32))!;
    private static readonly MethodInfo AsInt64Method = typeof(AstraValue).GetMethod(nameof(AstraValue.AsInt64))!;
    private static readonly MethodInfo AsFloatMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.AsFloat))!;
    private static readonly MethodInfo AsDoubleMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.AsDouble))!;
    private static readonly MethodInfo AsStringMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.AsString))!;
    private static readonly MethodInfo AsVector2Method = typeof(AstraValue).GetMethod(nameof(AstraValue.AsVector2))!;
    private static readonly MethodInfo AsObjectMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.AsObject))!;

    private static readonly MethodInfo FromBoolMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.FromBool))!;
    private static readonly MethodInfo FromInt64Method = typeof(AstraValue).GetMethod(nameof(AstraValue.FromInt64), [typeof(long)])!;
    private static readonly MethodInfo FromDoubleMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.FromDouble), [typeof(double)])!;
    private static readonly MethodInfo FromStringMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.FromString), [typeof(string)])!;
    private static readonly MethodInfo FromVector2Method = typeof(AstraValue).GetMethod(nameof(AstraValue.FromVector2), [typeof(Vector2)])!;
    private static readonly MethodInfo FromObjectMethod = typeof(AstraValue).GetMethod(nameof(AstraValue.FromObject), [typeof(object)])!;

    public static Func<AstraValue[], AstraValue> Compile(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var argsParam = Expression.Parameter(typeof(AstraValue[]), "args");
        var parameters = method.GetParameters();
        var callArgs = new List<Expression>();

        var argOffset = 0;
        Expression? instanceExpr = null;

        // If instance method, target is args[0]
        if (!method.IsStatic)
        {
            var targetArrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(0));
            instanceExpr = ConvertValueToType(targetArrayAccess, method.DeclaringType!);
            argOffset = 1;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i + argOffset));
            var converted = ConvertValueToType(arrayAccess, p.ParameterType);
            callArgs.Add(converted);
        }

        Expression callExpr = method.IsStatic
            ? Expression.Call(method, callArgs)
            : Expression.Call(instanceExpr!, method, callArgs);

        Expression returnExpr;
        if (method.ReturnType == typeof(void))
        {
            var nullConst = Expression.Constant(AstraValue.Null);
            returnExpr = Expression.Block(callExpr, nullConst);
        }
        else
        {
            returnExpr = ConvertTypeToAstraValue(callExpr, method.ReturnType);
        }

        var lambda = Expression.Lambda<Func<AstraValue[], AstraValue>>(returnExpr, argsParam);
        return lambda.Compile();
    }

    private static Expression ConvertValueToType(Expression valueExpr, Type targetType)
    {
        if (targetType == typeof(AstraValue)) return valueExpr;
        if (targetType == typeof(bool)) return Expression.Call(valueExpr, AsBoolMethod);
        if (targetType == typeof(byte)) return Expression.Convert(Expression.Call(valueExpr, AsInt32Method), typeof(byte));
        if (targetType == typeof(sbyte)) return Expression.Convert(Expression.Call(valueExpr, AsInt32Method), typeof(sbyte));
        if (targetType == typeof(short)) return Expression.Convert(Expression.Call(valueExpr, AsInt32Method), typeof(short));
        if (targetType == typeof(ushort)) return Expression.Convert(Expression.Call(valueExpr, AsInt32Method), typeof(ushort));
        if (targetType == typeof(int)) return Expression.Call(valueExpr, AsInt32Method);
        if (targetType == typeof(uint)) return Expression.Convert(Expression.Call(valueExpr, AsInt64Method), typeof(uint));
        if (targetType == typeof(long)) return Expression.Call(valueExpr, AsInt64Method);
        if (targetType == typeof(ulong)) return Expression.Convert(Expression.Call(valueExpr, AsInt64Method), typeof(ulong));
        if (targetType == typeof(float)) return Expression.Call(valueExpr, AsFloatMethod);
        if (targetType == typeof(double)) return Expression.Call(valueExpr, AsDoubleMethod);
        if (targetType == typeof(string)) return Expression.Call(valueExpr, AsStringMethod);
        if (targetType == typeof(Vector2)) return Expression.Call(valueExpr, AsVector2Method);

        // Object or reference / interface / struct type
        var asObj = Expression.Call(valueExpr, AsObjectMethod);
        return Expression.Convert(asObj, targetType);
    }

    private static Expression ConvertTypeToAstraValue(Expression expr, Type returnType)
    {
        if (returnType == typeof(AstraValue)) return expr;
        if (returnType == typeof(bool)) return Expression.Call(FromBoolMethod, expr);
        if (returnType == typeof(byte) || returnType == typeof(sbyte) ||
            returnType == typeof(short) || returnType == typeof(ushort) ||
            returnType == typeof(int) || returnType == typeof(uint) ||
            returnType == typeof(long))
        {
            var castToLong = Expression.Convert(expr, typeof(long));
            return Expression.Call(FromInt64Method, castToLong);
        }
        if (returnType == typeof(float) || returnType == typeof(double))
        {
            var castToDouble = Expression.Convert(expr, typeof(double));
            return Expression.Call(FromDoubleMethod, castToDouble);
        }
        if (returnType == typeof(string)) return Expression.Call(FromStringMethod, expr);
        if (returnType == typeof(Vector2)) return Expression.Call(FromVector2Method, expr);

        var boxed = Expression.Convert(expr, typeof(object));
        return Expression.Call(FromObjectMethod, boxed);
    }
}
