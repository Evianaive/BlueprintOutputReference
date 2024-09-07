# BlueprintOutputReference
## This plugin allows the user to export variables as references to the blueprint in Cpp, which can not be done before, by marking some metadata
## 这个插件允许用户在C++中定义输出引用的蓝图函数，这在正常的蓝图中是做不到的

将这个actor拖到场景中点击test按钮。 drag the actor to the level and press the test button
![image](https://github.com/user-attachments/assets/46a218c0-5caf-4c53-bc55-8138722e134c)


你可以看到屏幕上的打印结果。 You can see such print results on the screen
![image](https://github.com/user-attachments/assets/5f41b70a-2970-4b8d-a688-e6f8bf5af8d7)

蓝图会将函数的引用入参作为输出使用，但这样实际上输出的值而不是引用。如果需要输出引用，我们实际上要传入T*&。 blueprint function uses reference param as output but does not return value by reference unless we pass T*& into the function.

![image](https://github.com/user-attachments/assets/c8809337-aaaa-4d4f-af0e-b86a58a989ab)

以下是可以被这个插件导出到蓝图的C++输出引用的函数实现的注意事项。 these are some rules you need to follow when writing a function that can be exported to a blueprint by this plugin

1.需要输出引用的参数需要标记meta=(Ptr="true"). marking meta=(Ptr="true") on params that need to export by reference

2.函数标记meta=(BlueprintPtr="true",BlueprintInternalUseOnly="true")，不需要标记Blueprintable. marking meta=(BlueprintPtr="true",BlueprintInternalUseOnly="true") on function. Blueprintable is not needed.

3.函数内将传入的参数的类型视为uint64*&来处理, process those params as uint64*&

函数在蓝图中使用的形式如下 The function looks like this in the blueprint
![image](https://github.com/user-attachments/assets/fb03140b-f4be-4dfb-93a8-86424a0f7010)
