﻿// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "K2Node_CallFunction.h"
#include "UObject/Object.h"
#include "K2Node_CallFunctionOutputPtr.generated.h"

/**
 * 
 */
UCLASS()
class BLUEPRINTOUTPUTREFERENCEKISMET_API UK2Node_CallFunctionOutputPtr : public UK2Node_CallFunction
{
	GENERATED_BODY()
	virtual void AllocateDefaultPins() override;
	virtual void GetMenuActions(FBlueprintActionDatabaseRegistrar& ActionRegistrar) const override;
	virtual void GetMenuEntries(FGraphContextMenuBuilder& ContextMenuBuilder) const override;
	
	virtual FNodeHandlingFunctor* CreateNodeHandler(FKismetCompilerContext& CompilerContext) const override;
	virtual void ExpandNode(FKismetCompilerContext& CompilerContext, UEdGraph* SourceGraph) override;

	void UpdatePtrPinPairs();
public:
	struct FNonReferenceState
	{
		UEdGraphPin* MainPin;
		UEdGraphPin* PtrPin;
		UEdGraphPin* ReferencePin;
		UEdGraphPin* NonReferencePin;
	};
	TArray<FNonReferenceState> OutputPtrPinPairs;
	struct FRelatedTerminals
	{
		FBPTerminal* PtrTerm;
		FBPTerminal* RefTerm;
		FBPTerminal* NonRefTerm;
	};
	TMap<FBPTerminal*,FRelatedTerminals> OutputPtrPinTermMaps;
};