// Known source for probe-metadata-flags.cs. Every declaration states the modifier it was WRITTEN
// with, so the probe can print that beside the flags the compiler emitted and the signature the
// shipped reader renders. Three columns, one row per member, and any disagreement is a defect.
//
// Add a case here when a metadata shape is in question. Guessing what the compiler emits is how
// this reader twice claimed an override relationship that does not exist.

namespace FlagTruth;

using System;
using System.Collections;
using System.Collections.Generic;

public interface IThing
{
    void Act();                     // source: abstract (interface member)

    int Value { get; }              // source: abstract (interface member)

    event EventHandler? Fired;      // source: abstract (interface member)
}

// Implicitly implementing an interface: the source writes NO modifier. The compiler emits
// Final|Virtual|NewSlot, which is NOT 'sealed override' -- that is Final|Virtual without NewSlot,
// because an override reuses its base slot and a new slot is by definition not one.
public class ImplicitThing : IThing
{
    public void Act() { }                       // source: (none)

    public int Value { get; set; }              // source: (none) -- getter implements, setter plain

    public int PrivateSetter { get; private set; }  // source: (none), setter not visible

    public event EventHandler? Fired;           // source: (none)
}

// The shapes this reaches in practice.
public sealed class Practical : IEquatable<Practical>, IDisposable
{
    public bool Equals(Practical? other) => ReferenceEquals(this, other);   // source: (none)

    public override bool Equals(object? obj) => Equals(obj as Practical);   // source: override

    public override int GetHashCode() => 0;                                 // source: override

    public void Dispose() { }                                               // source: (none)
}

public class Enumerable : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();  // source: (none)

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();   // source: explicit, not visible
}

// The cases a collapse must not swallow.
public class Base
{
    public virtual void Act() { }               // source: virtual

    public virtual int V { get; set; }          // source: virtual

    public virtual event EventHandler? Fired;   // source: virtual
}

public class SealedOverride : Base
{
    public sealed override void Act() { }       // source: sealed override

    public sealed override int V { get; set; }  // source: sealed override
}

public class PlainOverride : Base
{
    public override void Act() { }              // source: override

    public override int V { get; set; }         // source: override
}

// An override that ALSO satisfies an interface reuses its base slot, so it is Virtual without
// NewSlot and must still read as an override.
public class OverrideThatImplements : Base, IThing
{
    public override void Act() { }              // source: override

    public int Value => 0;                      // source: (none)

    public event EventHandler? Fired;           // source: (none)
}

public abstract class AbstractShapes
{
    public abstract void Act();                 // source: abstract

    public abstract int V { get; }              // source: abstract
}

public class AbstractOverride : AbstractShapes
{
    public override void Act() { }              // source: override

    public override int V => 0;                 // source: override
}

public interface IStatic
{
    static abstract int Abstract { get; }        // source: static abstract

    static virtual int Virtual => 0;             // source: static virtual
}
