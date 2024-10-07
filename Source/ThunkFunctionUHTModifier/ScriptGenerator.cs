// Copyright Epic Games, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EpicGames.Core;
using EpicGames.UHT.Utils;
using EpicGames.UHT.Tables;
using EpicGames.UHT.Exporters.CodeGen;
using EpicGames.UHT.Types;

namespace Plugins.BlueprintOutputReference.ThunkFunctionUHTModifierUbtPlugin;


[UnrealHeaderTool]
class GeneratedCodeModifier
{
	[UhtExporter(Name = "BlueprintOutputReference", Description = "Modify the thunk function of UFUNCTION", Options = UhtExporterOptions.Default, ModuleName="BlueprintOutputReference")]
	private static void ScriptGeneratorExporter(IUhtExportFactory Factory)
	{
		// Factory.Session.LogInfo("This is a log44");
		new GeneratedCodeModifier(Factory).Modify();
	}
	
	GeneratedCodeModifier(IUhtExportFactory inFactory)
	{
		Factory = inFactory;
	}

	void Modify()
	{
		foreach (UhtPackage package in Factory.Session.Packages)
		{
			if (package.ToString().Contains("MassScriptSample"))
			{
				foreach (UhtType packageChild in package.Children)
				{
					UhtHeaderFile? headerFile = (UhtHeaderFile?)packageChild;
					if (headerFile == null)
						continue;
					ModifyHeaderGenFile(headerFile);
				}
			}
		}
	}

	void ModifyHeaderGenFile(UhtHeaderFile headerFile)
	{
		// Function Line Map Cache
		string[] allLines = {};
		Dictionary<string,int> lineIndices = new Dictionary<string,int>();
	
		string cppFilePath = Path.Combine(
			headerFile.Package.Module.OutputDirectory, 
			headerFile.FileNameWithoutExtension) + ".gen.cpp";
		Factory.Session.LogInfo(cppFilePath);
		Func<string, string, string, string> PARAM_PASSED_BY_REF = (string ParamName, string PropertyType, string ParamType)
			=> String.Format("\tuint64 {0}Temp;{2}& {0} = Stack.StepCompiledInRef<{1}, {2}>(&{0}Temp);", ParamName,
				PropertyType, ParamType);
		foreach (UhtType type in headerFile.Children)
		{
			UhtClass? classObj = (UhtClass?)type;
			if (classObj == null)
			{
				Factory.Session.LogInfo("  NotClass");
				continue;
			}

			foreach (UhtFunction function in classObj.Functions)
			{
				Factory.Session.LogInfo("  ", function.ToString());
				if (!function.MetaData.ContainsKey("BlueprintPtr"))
					continue;
				//Lazy Loading and Init .gen.cpp file
				if (allLines.Length == 0)
				{
					allLines = File.ReadAllLines(cppFilePath);
					for (int i = 0; i < allLines.Length; i++)
					{
						if (allLines[i].Contains("DEFINE_FUNCTION("))
						{
							int funcNameStart = allLines[i].IndexOf("::exec",15, StringComparison.CurrentCulture);
							if(funcNameStart==-1)
								Factory.Session.LogInfo("Failed to parse func");
							lineIndices.Add(allLines[i].Substring(funcNameStart+6, allLines[i].Length - 7 - funcNameStart),i);
						}
					}
				}
				if (allLines.Length == 0)
					Factory.Session.LogError("Failed to load .gen.cpp file!");					
					
				int lineIndex = lineIndices[function.CppImplName];
				Factory.Session.LogInfo("    ",lineIndex.ToString());
				lineIndex = lineIndex + 1;
				foreach (UhtType parameter in function.ParameterProperties.Span)
				{
					lineIndex++;
					UhtProperty? property = (UhtProperty?)parameter;
					if (property == null)
						continue;

					if (!property.MetaData.ContainsKey("Ptr"))
						continue;
					
					if (property.ArrayDimensions!=null)
					{
						//Static array with certain count
						// builder.AppendFunctionThunkParameterArrayType(this).Append(',');
						Factory.Session.LogError("Static Array Property is not supported");
						continue;
					}
					
					if (!property.PropertyFlags.HasAnyFlags(EPropertyFlags.OutParm))
						continue;
					
					bool pGetPassAsNoPtr = GetValue<bool>("PGetPassAsNoPtr", property);
					if (pGetPassAsNoPtr)
					{
						// builder.Append("_REF_NO_PTR");
						Factory.Session.LogError("REF_NO_PTR is not supported");
						continue;
					}
					
					
					using BorrowStringBuilder borrower = new(StringBuilderCache.Big);
					StringBuilder builder = borrower.StringBuilder;
					UhtPGetArgumentType argType = GetValue<UhtPGetArgumentType>("PGetTypeArgument", property);
					switch (argType)
					{
						case UhtPGetArgumentType.None:
							break;
						case UhtPGetArgumentType.EngineClass:
							builder.Append('F').Append(property.EngineClassName);
							break;
						case UhtPGetArgumentType.TypeText:
							builder.AppendPropertyText(property, UhtPropertyTextType.FunctionThunkParameterArgType);
							break;
					}

					string paramType = builder.ToString();
					builder.Clear();
					builder.AppendFunctionThunkParameterName(property);
					string paramName = builder.ToString();
					
					string? pGetMacroText = GetValue<string>("PGetMacroText", property);
					if (pGetMacroText == null)
					{
						Factory.Session.LogError("failed to get pGetMacroText");
						continue;
					}
					Factory.Session.LogInfo(pGetMacroText);

					switch (pGetMacroText)
					{
						case "STRUCT":   allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, "FStructProperty", paramType);break;
						case "PROPERTY": allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, paramType, paramType+"::TCppType");break;
						case "TARRAY":   allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, "FArrayProperty", "TArray<"+paramType+">");break;
						case "TMAP":     allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, "FMapProperty", "TMap<"+paramType+">");break;
						case "TSET":     allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, "FSetProperty", "TSet<"+paramType+">");break;
						case "ENUM":     allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, "FEnumProperty", paramType);break;
					}
				}
			}
		}
		if(allLines.Length>0)
			File.WriteAllLines(cppFilePath,allLines);
	}

    static T? GetValue<T>(string name, UhtProperty instance)
	{
		PropertyInfo? prop = instance.GetType().GetProperty(
			name,
			BindingFlags.Instance |BindingFlags.Public |BindingFlags.NonPublic
			|BindingFlags.GetProperty|BindingFlags.Static|BindingFlags.GetField);
		if (prop != null)
		{
			return (T?)prop.GetValue(instance);
		}
		return default;
	}
	
	private IUhtExportFactory Factory;
}

