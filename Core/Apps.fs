﻿namespace Lloyd.Core.Apps

open Lloyd.Core
open Lloyd.Core.UI

module Editor =
    type 'a Model = {Label:string; Previous:(EventID * 'a) option; Latest:(EventID * 'a) option; Edit:'a option option; Invalid:string option}
                    member m.Current = m.Edit |> Option.getElseFun (fun () -> Option.map snd m.Latest)

    let init property =
        {Label=property.Name+":"; Previous=None; Latest=None; Edit=None; Invalid=None}

    type 'a Msg =
        | Edit of 'a option
        | Reset
        | Update of 'a Events
        | Invalid of string option

    let update msg model =
        match msg with
        | Edit e -> {model with Edit=if e=Option.map snd model.Latest then None else Some e}
        | Reset -> {model with Edit=None}
        | Update l ->
            let latest = List1.head l |> mapSnd List1.head |> Some
            {model with
                Previous = List1.tail l |> List.tryHead |> Option.map (mapSnd List1.head)
                Latest = latest
                Edit = if Option.map snd latest |> Some=model.Edit then None else model.Edit
            }
        | Invalid i -> {model with Invalid=i}

    let tooltip (model:'a Model) =
        let versioning =
            match model.Latest with
            | None -> None
            | Some (eid,v) ->
                let t = sprintf "Latest  :  %-25s%A" (string v) eid
                match model.Previous with
                | None -> Some t
                | Some (eid,v) -> sprintf "%s\nPrevious:  %-25s%A" t (string v) eid |> Some
        match model.Invalid, versioning with
        | Some i, Some v -> i+"\n\n"+v |> Some
        | Some i, None -> Some i
        | None, Some v -> Some v
        | None, None -> None
        

    let view inputUI model =
        let colour = match model.Invalid with | None -> Black | Some _ -> Red
        UI.div [Vertical] [
            UI.text [Bold;Tooltip (tooltip model); TextColour colour] model.Label
            inputUI model.Current |> UI.map Edit
        ]

    let app inputUI property = UI.appSimple (fun () -> init property) update (view inputUI)

    let eventUpdate property msg model =
        match Property.tryGetEvents property msg with
        | None -> model
        | Some events -> update (Update events) model

    let updateAndValidate property validator model latest edits msg =
        let model = update msg model
        match model.Edit with
        | None -> model, Property.validateEdit validator latest edits
        | Some e ->
            match Property.validate property e with
            | Ok _ -> {model with Invalid=None}, Property.validateEdit validator latest edits
            | Error (k,v) ->
                let validation =
                    match Property.validateEdit validator latest edits with
                    | Ok _ -> Error [k,v]
                    | Error l -> if List.exists (fst>>(=)k) l then Error l else (k,v)::l |> Error
                {model with Invalid=Some v}, validation

    let edit property model =
        match model.Edit with
        | Some b ->
            Property.validate property b
            |> Result.map (Property.set property >> List.singleton)
            |> Result.mapError List.singleton
        | None -> Ok []

module EditorSet =

    type Model<'a when 'a : comparison> = {
        Label: string
        Previous: (EventID * 'a Set) option
        Latest: (EventID * 'a Set) option
        Edit: 'a option list option
        Order: Map<'a,string>
    }

    let init property =
        {
            Label = property.Name+":"
            Previous = None
            Latest = None
            Edit = None
            Order = Map.empty
        }

    type Msg<'a when 'a : comparison> =
        | Insert
        | Remove of int
        | Modify of int * 'a option
        | Update of 'a SetEvent Events
        | Order of Map<'a,string>

    let current model =
        let latest() = Option.map (snd >> Set.toList >> List.sortBy (flip Map.tryFind model.Order) >> List.map Some) model.Latest
        model.Edit |> Option.orTryFun latest |> Option.getElse []

    let update msg model =
        match msg with
        | Insert -> {model with Edit= None::current model |> Some}
        | Remove i -> {model with Edit= current model |> List.removei i |> Some}
        | Modify (i,v) -> {model with Edit= current model |> List.replacei i v |> Some}
        | Update l ->
            let latest = SetEvent.toSet l
            {model with
                Previous = List1.tail l |> List1.tryOfList |> Option.map (fun l -> List1.head l |> fst, SetEvent.toSet l)
                Latest = List1.head l |> fst |> addSnd latest |> Some
                Edit =  match model.Edit with
                        | Some l when List.forall Option.isSome l && List.map Option.get l |> Set.ofList = latest -> None
                        | _ -> model.Edit
            }
        | Order m -> {model with Order=m}

    let view inputUI model =
        let header = UI.div [Horizontal] [UI.text [Bold; Width 150] model.Label ; UI.button [Width 20] "+" Insert]
        let item i a = UI.div [Horizontal] [inputUI [Width 150] a |> UI.map (fun v -> Modify(i,v)); UI.button [Width 20] "-" (Remove i)]
        let items = current model |> List.mapi item
        header::items |> UI.div [Vertical]

    let updateProperty property msg model =
        match Property.tryGetEvents property msg with
        | None -> model
        | Some events -> update (Update events) model

    let edit (property:Property<'a,'b SetEvent>) (model:'b Model) =
        match model.Edit with
        | Some l ->
            let before = model.Latest |> Option.map snd |> Option.getElse Set.empty
            List.choose id l |> Set.ofList |> SetEvent.difference before |> List.map (Property.set property)
        | None -> []
