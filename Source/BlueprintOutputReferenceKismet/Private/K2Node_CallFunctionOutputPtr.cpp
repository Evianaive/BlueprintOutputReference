// Fill out your copyright notice in the Description page of Project Settings.


#include "K2Node_CallFunctionOutputPtr.h"

#include "BlueprintActionDatabaseRegistrar.h"
#include "BlueprintTypePromotion.h"
#include "K2Node_Message.h"
#include "BlueprintOutputReference/Public/DereferencePtrFunctionLibrary.h"
#include "Kismet2/KismetEditorUtilities.h"

#include "Editor/BlueprintGraph/Public/CallFunctionHandler.h"
#include "Editor/BlueprintGraph/Private/CallFunctionHandler.cpp"
#include "Editor/BlueprintGraph/Private/PushModelHelpers.cpp"

#define LOCTEXT_NAMESPACE "CallFunctionOutputPtr"


struct FBlueprintCompiledStatement;

class FKCHandler_CallFunctionOutputPtr : public FKCHandler_CallFunction
{
public:
	static inline FString Ends = TEXT("_ArrayPropHackMark");
	FKCHandler_CallFunctionOutputPtr(FKismetCompilerContext& InCompilerContext)
		: FKCHandler_CallFunction(InCompilerContext)
	{
	}
	UK2Node_CallFunctionOutputPtr* CurNode{nullptr};
	virtual void CheckIfFunctionIsCallable(UFunction* Function, FKismetFunctionContext& Context, UEdGraphNode* Node) override
	{
		// Verify that the function is not a Blueprint callable function
		if (Function->HasAnyFunctionFlags(FUNC_BlueprintCallable) && (Function->GetOuter() != Context.NewClass))
		{
			const bool bIsParentFunction = Node && Node->IsA<UK2Node_CallParentFunction>();
			if (!bIsParentFunction && Function->GetName().Find(UEdGraphSchema_K2::FN_ExecuteUbergraphBase.ToString()))
			{
				CompilerContext.MessageLog.Error(*FText::Format(NSLOCTEXT("KismetCompiler", "ShouldNotCallFromBlueprint_ErrorFmt", "Function '{0}' called from @@ should not be called from a Blueprint"), FText::FromString(Function->GetName())).ToString(), Node);
			}
		}
	}
	
	virtual void RegisterNets(FKismetFunctionContext& Context, UEdGraphNode* Node) override
	{
		// UK2Node_CallFunctionOutputPtr* CurNode = CastChecked<UK2Node_CallFunctionOutputPtr>(Node);
		
		/* auto register other pin(literal) by default method*/
		CurNode = CastChecked<UK2Node_CallFunctionOutputPtr>(Node);
		FKCHandler_CallFunction::RegisterNets(Context, Node);
		CurNode = nullptr;
	}
	virtual void RegisterNet(FKismetFunctionContext& Context, UEdGraphPin* Pin) override
	{
		// This net is an output from a function call
		if(auto* FindResult =	CurNode->OutputPtrPinPairs
			.FindByPredicate([Pin](const UK2Node_CallFunctionOutputPtr::FNonReferenceState& Pair)
			{
				return Pair.MainPin == Pin;
			}))
		{
			// ptr as int64
			FKCHandler_CallFunction::RegisterNet(Context,FindResult->PtrPin);
			FBPTerminal** PtrTermResult = Context.NetMap.Find(FindResult->PtrPin);
			if(PtrTermResult==nullptr)
			{
				// Todo Return Error!
			}
			
			FBPTerminal* Term = new FBPTerminal();
			Context.InlineGeneratedValues.Add(Term);			
			Term->CopyFromPin(FindResult->MainPin, Context.NetNameMap->MakeValidName(FindResult->MainPin));
			Context.NetMap.Add(FindResult->MainPin, Term);
			// inline struct
			if(FindResult->ReferencePin == FindResult->MainPin)
			{				
				auto& RelatedTerm = CurNode->OutputPtrPinTermMaps.FindOrAdd(Term,{*PtrTermResult,Term,nullptr});
				if(FindResult->NonReferencePin)
				{
					FKCHandler_CallFunction::RegisterNet(Context,FindResult->NonReferencePin);
					if(FBPTerminal** NonRefTerm = Context.NetMap.Find(FindResult->NonReferencePin))
					{
						RelatedTerm.NonRefTerm = *NonRefTerm;
					}						
				}
				return;
			}
		}
		FKCHandler_CallFunction::RegisterNet(Context, Pin);
	}

	virtual void Compile(FKismetFunctionContext& Context, UEdGraphNode* Node) override
	{
		UFunction* ConvertPtrFunction = UDereferencePtrFunctionLibrary::StaticClass()
		->FindFunctionByName(GET_FUNCTION_NAME_CHECKED(UDereferencePtrFunctionLibrary,DereferencePtr));

		UFunction* CopyValueFromPtrFunction = UDereferencePtrFunctionLibrary::StaticClass()
		->FindFunctionByName(GET_FUNCTION_NAME_CHECKED(UDereferencePtrFunctionLibrary,DereferencePtrToVar));
		
		CurNode = CastChecked<UK2Node_CallFunctionOutputPtr>(Node);		
		FKCHandler_CallFunction::Compile(Context, Node);
		if(auto StatementsResult = Context.StatementsPerNode.Find(Node))
		{
			auto InsertPtrConvert = [&,this](FBPTerminal*& FinalOutput)
			{
				if(auto RHSPtrResult = CurNode->OutputPtrPinTermMaps.Find(FinalOutput))
				{
					FinalOutput = RHSPtrResult->PtrTerm;
					// We should not use AppendStatementForNode because the state is inlineGenerated. It does
					// not need to generate a piece of code for other to jump to
					// FBlueprintCompiledStatement& PtrConvert = Context.AppendStatementForNode(Node);
					FBlueprintCompiledStatement* Result = new FBlueprintCompiledStatement();
					Context.AllGeneratedStatements.Add(Result);
					Result->Type = KCST_CallFunction;
					Result->RHS.Add(RHSPtrResult->PtrTerm);
					// PtrConvert.LHS = FinalOutput;
					RHSPtrResult->RefTerm->InlineGeneratedParameter = Result;
					
					if(RHSPtrResult->NonRefTerm)
					{
						Result->FunctionToCall = CopyValueFromPtrFunction;
						Result->RHS.Add(RHSPtrResult->NonRefTerm);
					}
					else
					{
						Result->FunctionToCall = ConvertPtrFunction;
					}
				}
			};
			const int32 CurrentStatementCount = StatementsResult->Num();			
			for (int i = 0; i<CurrentStatementCount;i++)
			{
				auto Statement = (*StatementsResult)[i];
				for(auto& RHS : Statement->RHS)
				{
					InsertPtrConvert(RHS);
				}
				InsertPtrConvert(Statement->LHS);
			}
		}
		CurNode = nullptr;
	}
};

void UK2Node_CallFunctionOutputPtr::AllocateDefaultPins()
{
	Super::AllocateDefaultPins();
	UpdatePtrPinPairs();
}

void UK2Node_CallFunctionOutputPtr::GetMenuActions(FBlueprintActionDatabaseRegistrar& ActionRegistrar) const
{
	// ContextMenuBuilder.AddActionList()
	auto IsInheritedBlueprintFunction = [](const UFunction* Function)
	{
		bool bIsBpInheritedFunc = false;
		if (UClass* FuncClass = Function->GetOwnerClass())
		{
			if (UBlueprint* BpOwner = Cast<UBlueprint>(FuncClass->ClassGeneratedBy))
			{
				FName FuncName = Function->GetFName();
				if (UClass* ParentClass = BpOwner->ParentClass)
				{
					bIsBpInheritedFunc = (ParentClass->FindFunctionByName(FuncName, EIncludeSuperFlag::IncludeSuper) != nullptr);
				}
			}
		}
		return bIsBpInheritedFunc;
	};
	// auto IsBlueprintInterfaceFunction = [](const UFunction* Function)
	// {
	// 	bool bIsBpInterfaceFunc = false;
	// 	if (UClass* FuncClass = Function->GetOwnerClass())
	// 	{
	// 		if (UBlueprint* BpOuter = Cast<UBlueprint>(FuncClass->ClassGeneratedBy))
	// 		{
	// 			FName FuncName = Function->GetFName();
	//
	// 			for (int32 InterfaceIndex = 0; (InterfaceIndex < BpOuter->ImplementedInterfaces.Num()) && !bIsBpInterfaceFunc; ++InterfaceIndex)
	// 			{
	// 				FBPInterfaceDescription& InterfaceDesc = BpOuter->ImplementedInterfaces[InterfaceIndex];
	// 				if(!InterfaceDesc.Interface)
	// 				{
	// 					continue;
	// 				}
	//
	// 				bIsBpInterfaceFunc = (InterfaceDesc.Interface->FindFunctionByName(FuncName) != nullptr);
	// 			}
	// 		}
	// 	}
	// 	return bIsBpInterfaceFunc;
	// };
	auto MakeMessageNodeSpawner = [](UFunction* InterfaceFunction)
	{
		check(InterfaceFunction != nullptr);
		check(FKismetEditorUtilities::IsClassABlueprintInterface(CastChecked<UClass>(InterfaceFunction->GetOuter())));

		UBlueprintFunctionNodeSpawner* NodeSpawner = UBlueprintFunctionNodeSpawner::Create(UK2Node_Message::StaticClass(), InterfaceFunction);
		check(NodeSpawner != nullptr);

		auto SetNodeFunctionLambda = [](UEdGraphNode* NewNode, FFieldVariant FuncField)
		{
			UK2Node_Message* MessageNode = CastChecked<UK2Node_Message>(NewNode);
			MessageNode->FunctionReference.SetFromField<UFunction>(FuncField.Get<UField>(), /*bIsConsideredSelfContext =*/false);
		};
		NodeSpawner->SetNodeFieldDelegate = UBlueprintFunctionNodeSpawner::FSetNodeFieldDelegate::CreateStatic(SetNodeFunctionLambda);

		NodeSpawner->DefaultMenuSignature.MenuName = FText::Format(LOCTEXT("MessageNodeMenuName", "{0} (Message)"), NodeSpawner->DefaultMenuSignature.MenuName);
		return NodeSpawner;
	};
	
	UClass* ActionKey = GetClass();
	if (!ActionRegistrar.IsOpenForRegistration(ActionKey))
	{
		return;
	}
	
	for (TObjectIterator<UClass> ClassIt; ClassIt; ++ClassIt)
	{
		UClass* const Class = (*ClassIt);
		for (TFieldIterator<UFunction> FunctionIt(Class, EFieldIteratorFlags::ExcludeSuper); FunctionIt; ++FunctionIt)
		{
			UFunction* Function = *FunctionIt;
			if(!Function->GetBoolMetaData(TEXT("BlueprintPtr")))
			{
				continue;
			}
			UE_LOG(LogTemp,Log,TEXT("Find TestPtr"));
			bool const bIsInheritedFunction = IsInheritedBlueprintFunction(Function);
			if (bIsInheritedFunction)
			{
				// inherited functions will be captured when the parent class is ran
				// through this function (no need to duplicate)
				continue;
			}

			// Apply general filtering for functions
			if(!FBlueprintActionDatabase::IsFunctionAllowed(Function, FBlueprintActionDatabase::EPermissionsContext::Node))
			{
				continue;
			}
			
			// bool const bIsBpInterfaceFunc = IsBlueprintInterfaceFunction(Function);
			// if (UEdGraphSchema_K2::FunctionCanBePlacedAsEvent(Function) && !bIsBpInterfaceFunc)
			// {
			// 	if (UBlueprintEventNodeSpawner* NodeSpawner = UBlueprintEventNodeSpawner::Create(Function))
			// 	{
			// 		ActionListOut.Add(NodeSpawner);
			// 	}
			// }
			
			// If this is a promotable function, and it has already been registered
			// than do NOT add it to the asset action database. We should
			// probably have some better logic for this, like adding our own node spawner
			const bool bIsRegisteredPromotionFunc =
				TypePromoDebug::IsTypePromoEnabled() &&
				FTypePromotion::IsFunctionPromotionReady(Function) &&
				FTypePromotion::IsOperatorSpawnerRegistered(Function);


			auto CanUserKismetCallFunction = [](const UFunction* Function)
			{
				return Function
				&& (!Function->HasAllFunctionFlags(FUNC_Delegate)
					&& Function->GetBoolMetaData(FBlueprintMetadata::MD_BlueprintInternalUseOnly)
					&& (!Function->HasMetaData(FBlueprintMetadata::MD_DeprecatedFunction) || GetDefault<UBlueprintEditorSettings>()->bExposeDeprecatedFunctions));
			};
			
			if (CanUserKismetCallFunction(Function))
			{
				// @TODO: if this is a Blueprint, and this function is from a 
				//        Blueprint "implemented interface", then we don't need to 
				//        include it (the function is accounted for in from the 
				//        interface class).
				// UBlueprintFunctionNodeSpawner* FuncSpawner = UBlueprintFunctionNodeSpawner::Create(Function);
				UBlueprintFunctionNodeSpawner* FuncSpawner = UBlueprintFunctionNodeSpawner::Create(ActionKey,Function);
			
				// Only add this action to the list of the operator function is not already registered. Otherwise we will 
				// get a bunch of duplicate operator actions
				if (!bIsRegisteredPromotionFunc)
				{
					ActionRegistrar.AddBlueprintAction(FuncSpawner);
				}

				if (FKismetEditorUtilities::IsClassABlueprintInterface(Class))
				{
					// Use the default function name
					ActionRegistrar.AddBlueprintAction(MakeMessageNodeSpawner(Function));
				}
			}
		}
	}
}

void UK2Node_CallFunctionOutputPtr::GetMenuEntries(FGraphContextMenuBuilder& ContextMenuBuilder) const
{
	Super::GetMenuEntries(ContextMenuBuilder);
}

FNodeHandlingFunctor* UK2Node_CallFunctionOutputPtr::CreateNodeHandler(FKismetCompilerContext& CompilerContext) const
{
	return new FKCHandler_CallFunctionOutputPtr(CompilerContext);
}

void UK2Node_CallFunctionOutputPtr::ExpandNode(FKismetCompilerContext& CompilerContext, UEdGraph* SourceGraph)
{
	UpdatePtrPinPairs();
	Super::ExpandNode(CompilerContext, SourceGraph);
	
	for (auto& Element : OutputPtrPinPairs)
	{
		// UEdGraphPin::CreatePin(CurNode);
		Element.PtrPin = CreatePin(EGPD_Output,
			UEdGraphSchema_K2::PC_Int64,
			FName(Element.MainPin->PinName.ToString()+TEXT("_Ptr")));
		Pins.RemoveAt(Pins.Num()-1);
		bool ConnectionHasRef = false;
		bool ConnectionHasNonRef = false;
		for(auto LinkedTo : Element.MainPin->LinkedTo)
		{
			ConnectionHasRef |= LinkedTo->PinType.bIsReference;
			ConnectionHasNonRef |= !LinkedTo->PinType.bIsReference;
		}
		if(ConnectionHasRef || ConnectionHasNonRef)
		{
			Element.ReferencePin = Element.MainPin;
			// for(auto LinkedTo : Element.ReferencePin->LinkedTo)
			// {
			// 	if(!LinkedTo->PinType.bIsReference)
			// 	{
			// 		for(auto& InLinkPin : LinkedTo->LinkedTo)
			// 		{
			// 			if(InLinkPin == Element.ReferencePin)
			// 			{
			// 				InLinkPin = Element.NonReferencePin;
			// 			}
			// 		}
			// 	}
			// }
		}
		if(ConnectionHasNonRef)
		{
			// Create Non Reference Pin
			Element.NonReferencePin = CreatePin(EGPD_Output,
			UEdGraphSchema_K2::PC_Int64,
			FName(Element.ReferencePin->PinName.ToString()+TEXT("_NonRef")));
			Pins.RemoveAt(Pins.Num()-1);
			Element.NonReferencePin->PinType = Element.ReferencePin->PinType;
			Element.NonReferencePin->PinType.bIsReference = false;
		}
	}
}

void UK2Node_CallFunctionOutputPtr::UpdatePtrPinPairs()
{
	auto Function = GetTargetFunction();
	
	TSet<FName> OutputPtrPinNames;
	for (TFieldIterator<FProperty> PropIt(Function); PropIt && (PropIt->PropertyFlags & CPF_Parm); ++PropIt)
	{
		FProperty* Param = *PropIt;
		// Todo don't add to set if Param is const T&
		if(Param->GetBoolMetaData(TEXT("Ptr")))
		{
			const FString& PinDisplayName = Param->GetMetaData(FBlueprintMetadata::MD_DisplayName);
			if (!PinDisplayName.IsEmpty())
			{
				OutputPtrPinNames.Add(FName(PinDisplayName));
			}
			else if (Function->GetReturnProperty() == Param && Function->HasMetaData(FBlueprintMetadata::MD_ReturnDisplayName))
			{
				OutputPtrPinNames.Add(FName(Function->GetMetaData(FBlueprintMetadata::MD_ReturnDisplayName)));
			}
			else
			{
				OutputPtrPinNames.Add(Param->GetFName());
			}
		}
	}
	OutputPtrPinPairs.Reset();
	for(auto Pin: Pins)
	{
		if(OutputPtrPinNames.Find(Pin->PinFriendlyName.IsEmpty()? Pin->PinName:FName(Pin->PinFriendlyName.ToString())))
		{
			// Pin->Direction = EGPD_Output;
			Pin->PinType.bIsReference = true;
			OutputPtrPinPairs.Add({Pin});
		}
	}
}


#undef LOCTEXT_NAMESPACE
