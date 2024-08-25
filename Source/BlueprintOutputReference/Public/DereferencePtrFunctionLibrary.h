// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "DereferencePtrFunctionLibrary.generated.h"

/**
 * 
 */
UCLASS()
class BLUEPRINTOUTPUTREFERENCE_API UDereferencePtrFunctionLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()
public:
	UFUNCTION(CustomThunk)
	static void DereferencePtr(int64 InPtr);
	DECLARE_FUNCTION(execDereferencePtr);
	
	UFUNCTION(CustomThunk)
	static void DereferencePtrToVar(int64 InPtr,UPARAM(ref)int32& Var);
	DECLARE_FUNCTION(execDereferencePtrToVar);
	
	UFUNCTION(BlueprintPure, meta=(DisplayName = "Test To String (Vector)", CompactNodeTitle = "->", BlueprintAutocast), Category="Utilities|String")
	static FString Conv_TestVectorToString(FVector InVec);
};
