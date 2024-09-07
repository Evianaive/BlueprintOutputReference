// Copyright 2024-2025 Evianaive. All Rights Reserved.

#include "DereferencePtrFunctionLibrary.h"


void UDereferencePtrFunctionLibrary::DereferencePtr(int64 InPtr)
{
}

DEFINE_FUNCTION(UDereferencePtrFunctionLibrary::execDereferencePtr)
{
	P_GET_PROPERTY(FInt64Property,InPtr)
	
	static FInt64Property Prop = FInt64Property(EC_InternalUseOnlyConstructor,nullptr);
	Stack.MostRecentProperty = &Prop;
	Stack.MostRecentPropertyAddress = reinterpret_cast<uint8*>(InPtr);

	P_FINISH;
	P_NATIVE_BEGIN;
	P_NATIVE_END;
}

void UDereferencePtrFunctionLibrary::DereferencePtrToVar(int64 InPtr, int32& Var)
{
}

DEFINE_FUNCTION(UDereferencePtrFunctionLibrary::execDereferencePtrToVar)
{
	// Read first param
	P_GET_PROPERTY(FInt64Property,InPtr)
	// Read second param
	Stack.MostRecentProperty = nullptr;
	Stack.MostRecentPropertyAddress = nullptr;
	Stack.Step(Stack.Object,nullptr);
	// change most recent property address for other function to de-reference
	Stack.MostRecentPropertyAddress = reinterpret_cast<uint8*>(InPtr);
	// copy value to result param for function param that passed by value
	if(Stack.MostRecentProperty && RESULT_PARAM)
		Stack.MostRecentProperty->CopyCompleteValueToScriptVM(RESULT_PARAM, reinterpret_cast<void*>(InPtr));
	P_FINISH;
	P_NATIVE_BEGIN;
	P_NATIVE_END;
}