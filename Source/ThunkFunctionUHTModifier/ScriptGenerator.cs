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
using EpicGames.UHT.Parsers;
using EpicGames.UHT.Tokenizer;
using EpicGames.UHT.Types;
using UnrealBuildBase;
using UnrealBuildTool;


public static class ReflectionHelper<TTarget>
{
	public static TField? GetField<TField>(TTarget instance, string fieldName)
	{
		FieldInfo? field = typeof(TTarget).GetField(
			fieldName,
			BindingFlags.NonPublic | BindingFlags.Instance);
        
		if (field == null)
			throw new ArgumentException($"Field {fieldName} not found");
            
		return (TField)field.GetValue(instance)!;
	}

	public static void SetField<TField>(TTarget instance, string fieldName, TField value)
	{
		FieldInfo? field = typeof(TTarget).GetField(
			fieldName,
			BindingFlags.NonPublic | BindingFlags.Instance);
            
		if (field == null)
			throw new ArgumentException($"Field {fieldName} not found");
            
		field.SetValue(instance, value);
	}
}


namespace Plugins.BlueprintOutputReference.ThunkFunctionUHTModifierUbtPlugin
{
	[UnrealHeaderTool]
	class GeneratedCodeModifier
	{
		[UhtExporter(Name = "BlueprintOutputReference", Description = "Modify the thunk function of UFUNCTION",
			Options = UhtExporterOptions.Default, ModuleName = "BlueprintOutputReference")]
		private static void ScriptGeneratorExporter(IUhtExportFactory Factory)
		{
			new GeneratedCodeModifier(Factory).Modify();
		}

		GeneratedCodeModifier(IUhtExportFactory inFactory)
		{
			Factory = inFactory;
		}

		void Modify()
		{
// #if UE_5_5_OR_LATER
			foreach (UhtModule package in Factory.Session.Modules)
	// #else
// 			foreach (UhtPackage package in Factory.Session.Packages)
// #endif
			{
				// if (package.Module.ToString().Contains("MassScriptSample"))
				{
					foreach (UhtHeaderFile headerFile in package.Headers)
					{
						ModifyHeaderGenFile(headerFile);
					}
				}
			}
		}

		void ModifyHeaderGenFile(UhtHeaderFile headerFile)
		{
			// Function Line Map Cache
			string[] allLines = { };
			Dictionary<string, int> lineIndices = new Dictionary<string, int>();

			string cppFilePath = Path.Combine(
// #if UE_5_5_OR_LATER
				headerFile.Module.Module.OutputDirectory,
// #else
				// headerFile.Package.Module.OutputDirectory,
// #endif
				headerFile.FileNameWithoutExtension) + ".gen.cpp";
			Factory.Session.LogInfo(cppFilePath);
			Func<string, string, string, string> PARAM_PASSED_BY_REF =
				(string ParamName, string PropertyType, string ParamType)
					=> String.Format("\tuint64 {0}Temp;{2}& {0} = Stack.StepCompiledInRef<{1}, {2}>(&{0}Temp);",
						ParamName,
						PropertyType, ParamType);
			
			foreach (UhtType type in headerFile.Children)
			{
				UhtClass? classObj = type as UhtClass;
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
								int funcNameStart = allLines[i].IndexOf("::exec", 15, StringComparison.CurrentCulture);
								if (funcNameStart == -1)
									Factory.Session.LogInfo("Failed to parse func");
								lineIndices.Add(
									allLines[i].Substring(funcNameStart + 6, allLines[i].Length - 7 - funcNameStart),
									i);
							}
						}
					}

					if (allLines.Length == 0)
						Factory.Session.LogError("Failed to load .gen.cpp file!");

					int lineIndex = lineIndices[function.CppImplName];
					Factory.Session.LogInfo("    ", lineIndex.ToString());
					lineIndex = lineIndex + 1;
					string outParamName = "";
					string outParamType = "";
					foreach (UhtType parameter in function.ParameterProperties.Span)
					{
						lineIndex++;
						UhtProperty? property = (UhtProperty?)parameter;
						if (property == null)
							continue;

						if (!property.MetaData.ContainsKey("Ptr"))
							continue;

						if (property.ArrayDimensions != null)
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

						outParamName = paramName;
						string PropertyType = paramType;
						outParamType = paramType;
						switch (pGetMacroText)
						{
							case "STRUCT":
								PropertyType = "FStructProperty";
								// inParamType = paramType;
								break;
							case "PROPERTY":
								outParamType = paramType + "::TCppType";
								break;
							case "TARRAY":
								PropertyType = "FArrayProperty";
								outParamType = "TArray<" + paramType + ">";
								break;
							case "TMAP":
								PropertyType = "FMapProperty";
								outParamType = "TMap<" + paramType + ">";
								break;
							case "TSET":
								PropertyType = "FSetProperty";
								outParamType = "TSet<" + paramType + ">";
								break;
							case "ENUM":
								PropertyType = "FEnumProperty";
								// inParamType = paramType;
								break;
						}
						allLines[lineIndex] = PARAM_PASSED_BY_REF(paramName, PropertyType, outParamType);
					}

					if (function.MetaData.ContainsKey("StaticMemberProperty"))
					{
						while (!allLines[lineIndex].Contains("P_NATIVE_END"))
						{
							if (allLines[lineIndex].Contains(function.SourceName))
							{
								allLines[lineIndex] =
									String.Format("\treinterpret_cast<{0}*&>({1}) = &{2};",
										outParamType,
										outParamName,
										function.SourceName.Substring(3));
								break;
							}
							lineIndex++;
						}
					}
				}
			}
			if (allLines.Length > 0)
				File.WriteAllLines(cppFilePath, allLines);
		}

		static T? GetValue<T>(string name, UhtProperty instance)
		{
			PropertyInfo? prop = instance.GetType().GetProperty(
				name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.GetProperty | BindingFlags.Static | BindingFlags.GetField);
			if (prop != null)
			{
				return (T?)prop.GetValue(instance);
			}
			return default;
		}

		private IUhtExportFactory Factory;
	}
// }
//
// namespace EpicGames.UHT.Parsers
// {

	/// <summary>
	/// USTRUCT parser object
	/// </summary>
	[UnrealHeaderTool]
	public static class UhtStaticPropertyParser
	{
		[UhtKeyword(Extends = UhtTableNames.Class, Keyword = "USTATICPROPERTY")]
		// [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Attribute accessed method")]
		// [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Attribute accessed method")]
		private static UhtParseResult USTATICPROPERTYKeyword(UhtParsingScope parentScope, UhtParsingScope actionScope,
			ref UhtToken token)
		{
			actionScope.Session.LogInfo(actionScope.ScopeType.ToString());
			UhtFunction function = new(parentScope.HeaderFile, parentScope.ScopeType, token.InputLine);

			function.FunctionType = UhtFunctionType.Function;
			// Todo Flags are incomplete?
			{
				function.FunctionFlags |= EFunctionFlags.Static;
				function.FunctionExportFlags |= UhtFunctionExportFlags.CppStatic;
			}
			function.FunctionFlags |= EFunctionFlags.Native;
			
			/*Custom parser*/
			UhtPropertyParser currentParser = parentScope.HeaderParser.GetCachedPropertyParser();

			Func<UhtPropertyParser, UhtParsingScope, EPropertyFlags, UhtPropertyParseOptions, UhtPropertyCategory,
				UhtPropertyParser> Parse =
				(thisParser, topScope, disallowPropertyFlags, options, category) =>
				{
					// Initialize the property context
					using UhtThreadBorrower<UhtPropertySpecifierContext> borrower = new(true);
					UhtPropertySpecifierContext specifierContext = borrower.Instance;
					specifierContext.Type = topScope.ScopeType;
					specifierContext.TokenReader = topScope.TokenReader;
					specifierContext.AccessSpecifier = topScope.AccessSpecifier;
					specifierContext.MessageSite = topScope.TokenReader;
					specifierContext.PropertySettings.Reset(specifierContext.Type, 0, category, disallowPropertyFlags);
					specifierContext.MetaData = specifierContext.PropertySettings.MetaData;
					specifierContext.MetaNameIndex = UhtMetaData.IndexNone;
					specifierContext.SeenEditSpecifier = false;
					specifierContext.SeenBlueprintWriteSpecifier = false;
					specifierContext.SeenBlueprintReadOnlySpecifier = false;
					specifierContext.SeenBlueprintGetterSpecifier = false;

					// Initialize the settings
					ReflectionHelper<UhtPropertyParser>.SetField(thisParser,"_options",options);
					ReflectionHelper<UhtPropertyParser>.SetField(thisParser,"_category",category);
					ReflectionHelper<UhtPropertyParser>.SetField(thisParser,"_currentTokenReader",topScope.TokenReader);
					ReflectionHelper<UhtPropertyParser>.SetField(thisParser,"_currentTypeTokens",new List<UhtToken>());
					ReflectionHelper<UhtPropertyParser>.SetField(thisParser,"_currentTemplateDepth",0);

					using UhtMessageContext tokenContext = new(thisParser);
					// ParseInternal(topScope, specifierContext, propertyDelegate);
					
					UhtPropertySettings propertySettings = specifierContext.PropertySettings;
					IUhtTokenReader tokenReader = topScope.TokenReader;

					propertySettings.LineNumber = tokenReader.InputLine;
					
					// Parse type information including UPARAM that might appear in template arguments
					PreParseTypeInternal(specifierContext, false);
					
					// Todo Check Should these be done here
					tokenReader
						.Optional("inline")
						.Optional("static");
					
					#region token_read_type
					
					// // Handle MemoryLayout.h macros
					// bool hasWrapperBrackets = false;
					// UhtLayoutMacroType layoutMacroType = UhtLayoutMacroType.None;
					// if (_options.HasAnyFlags(UhtPropertyParseOptions.ParseLayoutMacro))
					// {
					// 	ref UhtToken layoutToken = ref tokenReader.PeekToken();
					// 	if (layoutToken.IsIdentifier())
					// 	{
					// 		if (s_layoutMacroTypes.TryGetValue(layoutToken.Value, out layoutMacroType))
					// 		{
					// 			tokenReader.ConsumeToken();
					// 			tokenReader.Require('(');
					// 			hasWrapperBrackets = tokenReader.TryOptional('(');
					// 			if (layoutMacroType.IsEditorOnly())
					// 			{
					// 				propertySettings.PropertyFlags |= EPropertyFlags.EditorOnly;
					// 				propertySettings.DefineScope |= UhtDefineScope.EditorOnlyData;
					// 			}
					// 		}
					// 	}
					//
					// 	// This exists as a compatibility "shim" with UHT4/5.0.  If the fetched token wasn't an identifier,
					// 	// it wasn't returned to the tokenizer.  So, just consume the token here.  In theory, this should be
					// 	// removed once we have a good deprecated system.
					// 	//@TODO - deprecate
					// 	else // if (LayoutToken.IsSymbol(';'))
					// 	{
					// 		tokenReader.ConsumeToken();
					// 	}
					// }
					
					//@TODO: Should flag as settable from a const context, but this is at least good enough to allow use for C++ land
					tokenReader.Optional("mutable");

					// Gather the type tokens and possibly the property name.
					UhtTokensUntilDelegate? _gatherTypeTokensDelegate = ReflectionHelper<UhtPropertyParser>.GetField<UhtTokensUntilDelegate>(thisParser, "_gatherTypeTokensDelegate");
					if (_gatherTypeTokensDelegate != null)
					{
						tokenReader.While(_gatherTypeTokensDelegate);
					}

					// Verify we at least have one type
					List<UhtToken> _currentTypeTokens = ReflectionHelper<UhtPropertyParser>.GetField<List<UhtToken>>(thisParser, "_currentTypeTokens") ?? throw new InvalidOperationException();
					if (_currentTypeTokens.Count < 1)
					{
						throw new UhtException(tokenReader, $"{propertySettings.PropertyCategory.GetHintText()}: Missing variable type or name");
					}
					
					#endregion
					
					// // Consume the wrapper brackets.  This is just an extra set
					// if (hasWrapperBrackets)
					// {
					// 	tokenReader.Require(')');
					// }
					if (ReflectionHelper<UhtPropertyParser>.GetField<UhtPropertyParseOptions>(thisParser,"_options").HasAnyFlags(UhtPropertyParseOptions.AddModuleRelativePath))
					{
						UhtParsingScope.AddModuleRelativePathToMetaData(propertySettings.MetaData, topScope.HeaderFile);
					}
					
					UhtToken nameToken = _currentTypeTokens[^1];
					// Todo is needed?
					_currentTypeTokens.RemoveAt(_currentTypeTokens.Count - 1);
					
                    ReadOnlyMemory<UhtToken> _typeTokens = new ReadOnlyMemory<UhtToken>(_currentTypeTokens.ToArray());
					propertySettings.SourceName = propertySettings.PropertyCategory == UhtPropertyCategory.Return ? "ReturnValue" : nameToken.Value.ToString();
					UhtProperty paramProperty = UhtPropertyParser.ResolveProperty(UhtPropertyResolvePhase.Parsing, propertySettings,
						topScope.HeaderFile.Data.Memory, _typeTokens) ?? new UhtPreResolveProperty(propertySettings, _typeTokens);

		
					paramProperty.PropertyFlags |= EPropertyFlags.Parm | EPropertyFlags.OutParm;
					paramProperty.MetaData.Add("Ptr","");
					
					function.AddChildDirectly(paramProperty);
					function.MetaData.Add("BlueprintPtr","");
					function.MetaData.Add("BlueprintInternalUseOnly",true);
					function.MetaData.Add("StaticMemberProperty","");

					function.SourceName ="Get"+paramProperty.SourceName;

					return thisParser;
				};
			
			Parse(currentParser, parentScope, 
				/*Todo ParmFlags??*/ EPropertyFlags.ParmFlags, 
				UhtPropertyParseOptions.ParseLayoutMacro 
					| UhtPropertyParseOptions.List 
					| UhtPropertyParseOptions.AddModuleRelativePath,
				UhtPropertyCategory.Member);

			// UhtSession session = parentScope.Module.Session;
			// session.TryGetPropertyType(copy.Value, out UhtPropertyType propertyType)

			// C++ UHT TODO - Skip any extra ';'.  This can be removed if we remove UhtHeaderfileParser.ParserStatement generating errors
			// when extra ';' are found.  Oddly, UPROPERTY specifically skips extra ';'
			parentScope.TokenReader.Require(';');
			while (true)
			{
				UhtToken nextToken = parentScope.TokenReader.PeekToken();
				if (!nextToken.IsSymbol(';'))
				{
					break;
				}
				parentScope.TokenReader.ConsumeToken();
			}
			
			// Get function or operator name.
			SetFunctionNames(function);
			AddFunction(function);
			return UhtParseResult.Handled;
		}
		
		#region FunctionFromEngine
		
		private static void SetFunctionNames(UhtFunction function)
		{
			// The source name won't have the suffix applied to delegate names, however, the engine name will
			// We use the engine name because we need to detect the suffix for delegates
			string functionName = function.EngineName;
			if (functionName.EndsWith(UhtFunction.GeneratedDelegateSignatureSuffix, StringComparison.Ordinal))
			{
				functionName = functionName[..^UhtFunction.GeneratedDelegateSignatureSuffix.Length];
			}

			function.UnMarshalAndCallName = "exec" + functionName;

			if (function.FunctionFlags.HasAnyFlags(EFunctionFlags.BlueprintEvent))
			{
				function.MarshalAndCallName = functionName;
				if (function.FunctionFlags.HasAllFlags(EFunctionFlags.BlueprintEvent | EFunctionFlags.Native))
				{
					function.CppImplName = function.EngineName + "_Implementation";
				}
			}
			else if (function.FunctionFlags.HasAllFlags(EFunctionFlags.Native | EFunctionFlags.Net))
			{
				function.MarshalAndCallName = functionName;
				if (function.FunctionFlags.HasAnyFlags(EFunctionFlags.NetResponse))
				{
					// Response function implemented by programmer and called directly from thunk
					function.CppImplName = function.EngineName;
				}
				else
				{
					if (function.CppImplName.Length == 0)
					{
						function.CppImplName = function.EngineName + "_Implementation";
					}
					else if (function.CppImplName == functionName)
					{
						function.LogError("Native implementation function must be different than original function name.");
					}

					if (function.CppValidationImplName.Length == 0 && function.FunctionFlags.HasAnyFlags(EFunctionFlags.NetValidate))
					{
						function.CppValidationImplName = function.EngineName + "_Validate";
					}
					else if (function.CppValidationImplName == functionName)
					{
						function.LogError("Validation function must be different than original function name.");
					}
				}
			}
			else if (function.FunctionFlags.HasAnyFlags(EFunctionFlags.Delegate))
			{
				function.MarshalAndCallName = "delegate" + functionName;
			}

			if (function.CppImplName.Length == 0)
			{
				function.CppImplName = functionName;
			}

			if (function.MarshalAndCallName.Length == 0)
			{
				function.MarshalAndCallName = "event" + functionName;
			}
		}
		private static void AddFunction(UhtFunction function)
		{
			function.Outer?.AddChild(function);
		}
		
		private static void PreParseTypeInternal(UhtPropertySpecifierContext specifierContext, bool isTemplateArgument)
		{
			UhtPropertySettings propertySettings = specifierContext.PropertySettings;
			IUhtTokenReader tokenReader = specifierContext.TokenReader;
			UhtSession session = specifierContext.Type.Session;

			// We parse specifiers when:
			//
			// 1. This is the start of a member property (but not a template)
			// 2. The UPARAM identifier is found
			bool isMember = propertySettings.PropertyCategory == UhtPropertyCategory.Member;
			bool parseSpecifiers = (isMember && !isTemplateArgument) || tokenReader.TryOptional("UPARAM");

			UhtSpecifierParser specifiers = UhtSpecifierParser.GetThreadInstance(specifierContext, "Variable",
				isMember ? session.GetSpecifierTable(UhtTableNames.PropertyMember) : session.GetSpecifierTable(UhtTableNames.PropertyArgument));
			if (parseSpecifiers)
			{
				specifiers.ParseSpecifiers();
			}
			if (propertySettings.PropertyCategory != UhtPropertyCategory.Member && !isTemplateArgument)
			{
				// const before the variable type support (only for params)
				if (tokenReader.TryOptional("const"))
				{
					propertySettings.PropertyFlags |= EPropertyFlags.ConstParm;
					propertySettings.MetaData.Add(UhtNames.NativeConst, "");
				}
			}

			// Process the specifiers
			if (parseSpecifiers)
			{
				specifiers.ParseDeferred();
			}
			
			// If we saw a BlueprintGetter but did not see BlueprintSetter or 
			// or BlueprintReadWrite then treat as BlueprintReadOnly
			if (specifierContext.SeenBlueprintGetterSpecifier && !specifierContext.SeenBlueprintWriteSpecifier)
			{
				propertySettings.PropertyFlags |= EPropertyFlags.BlueprintReadOnly;
			}

			if (propertySettings.MetaData.ContainsKey(UhtNames.ExposeOnSpawn))
			{
				propertySettings.PropertyFlags |= EPropertyFlags.ExposeOnSpawn;
			}

			if (!isTemplateArgument)
			{
				UhtAccessSpecifier accessSpecifier = specifierContext.AccessSpecifier;
				if (accessSpecifier == UhtAccessSpecifier.Public || propertySettings.PropertyCategory != UhtPropertyCategory.Member)
				{
					propertySettings.PropertyFlags &= ~EPropertyFlags.Protected;
					propertySettings.PropertyExportFlags |= UhtPropertyExportFlags.Public;
					propertySettings.PropertyExportFlags &= ~(UhtPropertyExportFlags.Private | UhtPropertyExportFlags.Protected);

					propertySettings.PropertyFlags &= ~EPropertyFlags.NativeAccessSpecifiers;
					propertySettings.PropertyFlags |= EPropertyFlags.NativeAccessSpecifierPublic;
				}
				else if (accessSpecifier == UhtAccessSpecifier.Protected)
				{
					propertySettings.PropertyFlags |= EPropertyFlags.Protected;
					propertySettings.PropertyExportFlags |= UhtPropertyExportFlags.Protected;
					propertySettings.PropertyExportFlags &= ~(UhtPropertyExportFlags.Public | UhtPropertyExportFlags.Private);

					propertySettings.PropertyFlags &= ~EPropertyFlags.NativeAccessSpecifiers;
					propertySettings.PropertyFlags |= EPropertyFlags.NativeAccessSpecifierProtected;
				}
				else if (accessSpecifier == UhtAccessSpecifier.Private)
				{
					propertySettings.PropertyFlags &= ~EPropertyFlags.Protected;
					propertySettings.PropertyExportFlags |= UhtPropertyExportFlags.Private;
					propertySettings.PropertyExportFlags &= ~(UhtPropertyExportFlags.Public | UhtPropertyExportFlags.Protected);

					propertySettings.PropertyFlags &= ~EPropertyFlags.NativeAccessSpecifiers;
					propertySettings.PropertyFlags |= EPropertyFlags.NativeAccessSpecifierPrivate;
				}
				else
				{
					throw new UhtIceException("Unknown access level");
				}
			}
		}
		#endregion
	}
}