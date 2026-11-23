/// WCF services (CoreWCF) and clients (System.ServiceModel): an operation of a service contract is addressed by its
/// action, which the client and the service agree on whatever the binding (HTTP, TCP). The service dispatches a
/// message on it, so it is the operation's route, and a client's call is a network exit point at the same address.
module Floorplan.Analysis.Wcf

open Floorplan.IL
open Floorplan.Catalog

/// The catalog rules for an attribute, matched as a call of its constructor.
let rulesOf (matcher: Matcher) (a: AttributeDef) : Rule list =
    matcher.Match
        { Type = a.Type
          Name = ".ctor"
          Key = $"[attribute]{a.Type.Assembly}:{a.Type.FullName}::.ctor"
          LocalKey = ""
          ParameterTypes = []
          ReturnType = "System.Void"
          HasThis = true
          GenericArity = 0
          TypeArguments = []
          DeclaringTypeArguments = [] }

let private hasRole (roles: string list) (a: AttributeDef) (matcher: Matcher) =
    rulesOf matcher a |> List.exists (fun r -> r.Payload |> List.exists (fun p -> List.contains p.Role roles))

/// [ServiceContract] (CoreWCF's or System.ServiceModel's): the catalog's "wcf-contract" role.
let contractAttribute (matcher: Matcher) (attributes: AttributeDef list) =
    attributes |> List.tryFind (fun a -> hasRole [ "wcf-contract" ] a matcher)

/// [OperationContract]: the "wcf-operation" role, or the client rule's "wcf-action".
let operationAttribute (matcher: Matcher) (attributes: AttributeDef list) =
    attributes |> List.tryFind (fun a -> hasRole [ "wcf-operation"; "wcf-action" ] a matcher)

let private named (a: AttributeDef) (name: string) =
    a.Named
    |> List.tryPick (fun (n, v) ->
        match v with
        | AttributeValue.Text t when n = name && t <> "" -> Some t
        | _ -> None)

/// The action of an operation `m` of contract `contract`: [OperationContract(Action = ...)], else
/// {Namespace}/{Name}/{operation}, where the namespace defaults to http://tempuri.org/, the contract's name to the
/// interface's, and the operation's to the method's, without the "Async" of a method returning a task.
let action (contract: TypeDef) (contractAttribute: AttributeDef) (m: MethodDef) (operation: AttributeDef) : string option =
    match named operation "Action" with
    | Some "*" -> None
    | Some a -> Some a
    | None ->
        let ns = defaultArg (named contractAttribute "Namespace") "http://tempuri.org/"
        let ns = if ns.EndsWith "/" then ns else ns + "/"
        let name = defaultArg (named contractAttribute "Name") contract.Ref.Name

        let operationName =
            match named operation "Name" with
            | Some n -> n
            | None when m.Ref.Name.EndsWith "Async" && m.Ref.Name.Length > 5 && m.Ref.ReturnType.StartsWith "System.Threading.Tasks.Task" ->
                m.Ref.Name.Substring(0, m.Ref.Name.Length - 5)
            | None -> m.Ref.Name

        Some $"{ns}{name}/{operationName}"

/// The action of a contract method, from its declaring interface's attributes.
let actionOf (set: AssemblySet) (matcher: Matcher) (m: MethodDef) : string option =
    match set.TryType m.Ref.Type, operationAttribute matcher m.Attributes with
    | Some(contract, _), Some operation ->
        contractAttribute matcher contract.Attributes
        |> Option.bind (fun c -> action contract c m operation)
    | _ -> None
