﻿namespace Lloyd.Core

open System
open System.Threading

type Result<'o,'e> =
    | Ok of 'o
    | Error of 'e

[<AutoOpen>][<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Result =
    let map f r = match r with |Ok x -> Ok (f x) |Error e -> Error e
    let bind binder r = match r with |Ok i -> binder i |Error e -> Error e
    let apply f x =
        match f,x with
        | Ok f, Ok v -> Ok (f v)
        | Error f, Ok _ -> Error f
        | Ok _, Error f -> Error [f]
        | Error f1, Error f2 -> Error (f2::f1)
    let (<*>) = apply
    let ofOption f o = match o with |None -> f() |> Error |Some v -> Ok v
    let toOption r = match r with |Ok o -> Some o |Error _ -> None
    let getElse v r = match r with |Ok o -> o |Error _ -> v
    type AttemptBuilder() =
        member __.Bind(v,f) = bind f v
        member __.Return v = Ok v
        member __.ReturnFrom o = o
    let attempt = AttemptBuilder()

[<AutoOpen>]
module Threading =
    let rec atomicUpdate update state =
        let oldState = !state
        let newState = update oldState
        if Interlocked.CompareExchange<_>(state, newState, oldState) |> LanguagePrimitives.PhysicalEquality oldState then oldState,newState
        else atomicUpdate update state
    let rec atomicUpdateQuery update state =
        let oldState = !state
        let newState,result = update oldState
        if Interlocked.CompareExchange<_>(state, newState, oldState) |> LanguagePrimitives.PhysicalEquality oldState then newState,result
        else atomicUpdateQuery update state
    let deferred t f =
        let nextTime = ref None
        let rec check() = async {
            atomicUpdate (Option.bind (fun (next,a) ->
                let now = DateTime.UtcNow
                if next>now then
                    async {
                        do! Async.Sleep(let ts=next-now in int ts.Milliseconds)
                        do! check()
                    } |> Async.Start
                    Some(next,a)
                else
                    async { f a } |> Async.Start
                    None
            )) nextTime |> ignore
        }
        fun a ->
            let oldNextTime,_ = atomicUpdate (fun _ -> let utc = DateTime.UtcNow in Some(utc.Add(t),a)) nextTime
            if Option.isNone oldNextTime then check() |> Async.Start
    let memoize f =
        let d = Collections.Generic.Dictionary(HashIdentity.Structural)
        fun a ->
            let t,b = d.TryGetValue a
            if t then b
            else let b = f a in d.Add(a,b); b
    let inline flip f b a = f a b
    let between a b x = (a<=x&&x<=b)||(a>=x&&x>=b)

module String =
    let empty = String.Empty
    let nonEmpty s = if String.IsNullOrWhiteSpace s then None else s.Trim() |> Some
    let inline tryParse (s:string) =
        let mutable r = Unchecked.defaultof<_>
        if (^a : (static member TryParse: string * ^a byref -> bool) (s, &r)) then Some r else None

module Option =
    let getElse v o = match o with | Some i -> i | None -> v
    let getElseFun f o = match o with | Some i -> i | None -> f()
    let orTry a o = match o with | None -> a | _ -> o
    let ofList l = match l with |[] -> None |l -> Some l

module Seq =
    let groupByFst s = Seq.groupBy fst s |> Seq.map (fun (k,l) -> k,Seq.map snd l)

module List =
    let tryCons o xs = match o with |None -> xs | Some x -> x::xs
    let tryAppend o xs = match o with |None -> xs | Some x -> x@xs
    let removei i l =
        let s1,s2 =List.splitAt i l
        s1 @ List.tail s2
    let replacei i a l =
        let s1,s2 =List.splitAt i l
        s1 @ a::List.tail s2

module Map =
    let incr m k = Map.add k (Map.tryFind k m |> Option.getElse 0 |> (+)1) m
    let decr m k = Map.add k (Map.tryFind k m |> Option.getElse 0 |> (+) -1) m
    let addOrRemove k o m = match o with | Some v -> Map.add k v m | None -> Map.remove k m
