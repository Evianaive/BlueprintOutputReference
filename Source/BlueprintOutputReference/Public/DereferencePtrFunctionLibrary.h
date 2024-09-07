// Copyright 2024-2025 Evianaive. All Rights Reserved.

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
};
