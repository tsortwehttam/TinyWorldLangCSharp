using System.Collections.Generic;
using TinyWorldLang.Values;

namespace TinyWorldLang.Cel
{
    /// <summary>Base class for CEL expression nodes (the subset TWL uses).</summary>
    internal abstract class CelExpr { }

    internal sealed class CelLit : CelExpr
    {
        public Value Value { get; }
        public CelLit(Value value) => Value = value;
    }

    internal sealed class CelIdent : CelExpr
    {
        public string Name { get; }
        public CelIdent(string name) => Name = name;
    }

    internal sealed class CelListExpr : CelExpr
    {
        public IReadOnlyList<CelExpr> Items { get; }
        public CelListExpr(IReadOnlyList<CelExpr> items) => Items = items;
    }

    /// <summary>A record literal: <c>{ key: expr, key: expr }</c>.</summary>
    internal sealed class CelRecordExpr : CelExpr
    {
        public IReadOnlyList<(string Key, CelExpr Value)> Fields { get; }
        public CelRecordExpr(IReadOnlyList<(string, CelExpr)> fields) => Fields = fields;
    }

    internal sealed class CelUnary : CelExpr
    {
        public string Op { get; }
        public CelExpr Operand { get; }
        public CelUnary(string op, CelExpr operand) { Op = op; Operand = operand; }
    }

    internal sealed class CelBinary : CelExpr
    {
        public string Op { get; }
        public CelExpr Left { get; }
        public CelExpr Right { get; }
        public CelBinary(string op, CelExpr left, CelExpr right) { Op = op; Left = left; Right = right; }
    }

    internal sealed class CelTernary : CelExpr
    {
        public CelExpr Cond { get; }
        public CelExpr IfTrue { get; }
        public CelExpr IfFalse { get; }
        public CelTernary(CelExpr cond, CelExpr ifTrue, CelExpr ifFalse)
        { Cond = cond; IfTrue = ifTrue; IfFalse = ifFalse; }
    }

    internal sealed class CelMember : CelExpr
    {
        public CelExpr Target { get; }
        public string Name { get; }
        public CelMember(CelExpr target, string name) { Target = target; Name = name; }
    }

    internal sealed class CelIndex : CelExpr
    {
        public CelExpr Target { get; }
        public CelExpr Index { get; }
        public CelIndex(CelExpr target, CelExpr index) { Target = target; Index = index; }
    }

    /// <summary>
    /// A call. <see cref="Target"/> is null for a global call like <c>size(x)</c> or
    /// <c>one(x)</c>; non-null for a method/macro call like <c>list.filter(...)</c> or
    /// <c>now.getFullYear()</c>.
    /// </summary>
    internal sealed class CelCall : CelExpr
    {
        public CelExpr? Target { get; }
        public string Name { get; }
        public IReadOnlyList<CelExpr> Args { get; }
        public CelCall(CelExpr? target, string name, IReadOnlyList<CelExpr> args)
        { Target = target; Name = name; Args = args; }
    }
}
