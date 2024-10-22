﻿// Copyright 2024-2025 Evianaive. All Rights Reserved.
using UnrealBuildTool;

public class BlueprintOutputReferenceKismet : ModuleRules
{
    public BlueprintOutputReferenceKismet(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = ModuleRules.PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicIncludePaths.AddRange(
            new string[] {
                // ... add public include paths required here ...
            }
            );
				
		
        PrivateIncludePaths.AddRange(
            new string[] {
                // ... add other private include paths required here ...
            }
            );
        
        PublicDependencyModuleNames.AddRange(
            new string[]
            {
                "Core",
            }
            );

        PrivateDependencyModuleNames.AddRange(
            new string[]
            {
                "CoreUObject",
                "Engine",
                "Slate",
                "SlateCore",
                "UnrealEd",
                "BlueprintGraph",
                "KismetCompiler",
                "Kismet",
                "BlueprintOutputReference"
            }
            );
		
		
        DynamicallyLoadedModuleNames.AddRange(
            new string[]
            {
                // ... add any modules that your module loads dynamically here ...
            }
            );
    }
}