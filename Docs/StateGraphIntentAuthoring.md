# StateGraph Intent and Operation authoring

The standard path uses explicit declarations. A generated State has no Intent
requirements. Add one `CoCoIntentConsume` for each value type the State reads and
one `CoCoOperationProvide` for each Section it writes. Sources and Operators stay
on the Host's explicit component lists.

## Module registration

Keep the unmanaged value, reducer, and factory in an engine-free assembly. For
example:

```csharp
public struct MoveIntent { public float Speed; }
public struct MoveReducer : ICoCoIntentReducer<MoveIntent>
{
    public MoveIntent Reduce(in MoveIntent current, in MoveIntent candidate) => candidate;
}
public sealed class MoveFactory : ICoCoIntentReducerFactory<MoveIntent, MoveReducer>
{
    public MoveReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
}
```

The module's Unity-facing assembly supplies the source and registrar. Reference
Core Contracts, StateFlow, StateGraph, and StateGraphHost there. Derive a public
parameterless registrar and export it from a source or registrar class:

```csharp
[CoCoIntentRegistration(typeof(MoveRegistrar))]
public sealed class MoveRegistrar :
    CoCoStandardIntentRegistrar<MoveIntent, MoveReducer, MoveFactory>
{
    public MoveRegistrar() : base(MoveIds.Intent, 1, 1UL) { }
    protected override MoveFactory CreateReducerFactory() => new MoveFactory();
}

public sealed class MoveSource : MonoBehaviour, ICoCoIntentFrameSource<MoveIntent>
{
    public float Speed;
    public bool TrySample(in CoCoTickFrame tick, out MoveIntent intent)
    {
        intent = new MoveIntent { Speed = Speed };
        return true;
    }
}
```

`MoveIds.Intent` is a valid stable `CoCoIntentId` owned by the module. The
constructor also declares contribution capacity and reducer semantics fingerprint.
`CreateReducerFactory` supplies a new factory for each Host. Discovery scans
loaded assemblies carrying contract metadata; neither Core nor the standard
provider has a list of Input, Camera, or project modules. Editor and runtime use
the same provider and registration metadata.

Standard registration requires one unique Intent identity per value type and
one unique type per identity. A registrar is instantiated once per provider;
its runtime binding is created separately for each Host. Custom registrars can
implement `ICoCoStandardIntentRegistrar` and return an
`ICoCoStandardIntentSourceBinding`; they must keep Host handles in that returned
binding, never in shared registrar fields.

## State authors

```csharp
[CoCoState("Move")]
[CoCoIntentConsume(typeof(MoveIntent))]
[CoCoOperationProvide(typeof(ILocomotionSection))]
public sealed class MoveLogic : CoCoStateLogic, ICoCoStateUpdate
{
    public void Update(CoCoStateExecutionContext context)
    {
        if (context.TryReadIntent(out MoveIntent move))
        {
            var author = new LocomotionOperationAuthor(context.Operations);
            author.Move(0f, move.Speed);
            author.Gravity(true);
        }
    }
}
```

`TryReadIntent<T>` reads only the current frozen frame and the current State's
compiled Intent requirements. Missing samples and undeclared types return false.
The existing `context.Intents` handle and `TryFirst` reads are filtered by the
same State declarations. They cannot read a different State's lane merely
because it appears in the graph union. Exact ID lookup is available through the
optional `ICoCoIntentFrameLookup`; old custom frame implementations need not
implement this interface, but type-based reads require exact lookup support.

`LocomotionOperationAuthor` exposes Move, Force, Look, Gravity, Jump, and Teleport.
`AnimOperationAuthor` accepts existing `AnimParameterCommand` and
`AnimTriggerCommand` values by lane. These readonly values hold a callback-scoped
`CoCoStateOperationWriter`, contain no Unity references, and never execute an
engine side effect. Normal declaration checks, composition rank, Claim
arbitration, and the Host Operator execution path still apply. A retained author
expires with its callback; undeclared or invalid writes reject the Tick.

## Host binding and diagnostics

The graph compiles the union of explicit requirements, then registers reducers
in manifest order. Source selection compares exact `ICoCoIntentFrameSource<T>`
contracts in the Host's Intent Sources list. Reordering that list does not change
which value type reaches a State. The standard path accepts one source per type.

| Failure | Diagnostic |
|---|---|
| No registrar for a declared type | `MissingIntentReducer` |
| Repeated declaration or conflicting registrar type/identity | `DuplicateIdentifier` |
| Abstract or unconstructible registrar | `InvalidIntentDescriptor` |
| Missing source or wrong generic source type | `DescriptorTypeMismatch`, including the expected type |
| Two matching sources, including a repeated reference | `DuplicateIdentifier` |
| Foreign or nested Host source | `DescriptorTypeMismatch`, Host boundary explanation |
| Extra explicit source not used by the compiled graph | `MissingDescriptor` from Host coverage validation |

Catalog errors expose `CoCoStandardBindingException.Diagnostic`; the editor's
analysis panel preserves the message, and the runtime bootstrap reports it.
Host setup errors remain in `LastDiagnostic`. Binding failures leave the Host
Created with no sampling or State callbacks.

## Existing projects

Explicit RawInputIntent states remain valid through `InputRawIntentRegistrar`.
The wizard no longer inserts that declaration automatically. The low-level
`TryRegisterIntent`, `TryBeginIntentBindings`, and indexed `TryBindIntentSource`
APIs remain available, including multiple IDs of the same value type and custom
multi-producer reducers. Such graphs use explicit project bindings. No Input or
Camera default is substituted.
