using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Mono.Cecil;
using Mono.Cecil.Cil;

using Xamarin.Forms.Build.Tasks;
using Xamarin.Forms.Xaml;

using static System.String;

namespace Xamarin.Forms.Core.XamlC
{
	class BindablePropertyConverter : ICompiledTypeConverter
	{
		public IEnumerable<Instruction> ConvertFromString(string value, ILContext context, BaseNode node)
		{
			var module = context.Body.Method.Module;

			if (IsNullOrEmpty(value)) {
				yield return Instruction.Create(OpCodes.Ldnull);
				yield break;
			}
			var bpRef = GetBindablePropertyFieldReference(value, module, node, context);
			yield return Instruction.Create(OpCodes.Ldsfld, bpRef);
		}

		public FieldReference GetBindablePropertyFieldReference(string value, ModuleDefinition module, BaseNode node, ILContext context)
		{
			FieldReference bpRef = null;
			string typeName = null, propertyName = null;

			var parts = value.Split('.');
			if (parts.Length == 1) {
				var parent = node.Parent?.Parent as IElementNode ?? (node.Parent?.Parent as IListNode)?.Parent as IElementNode;
				if (node.Parent.IsType(typeof(Setter)) ||
					node.Parent.IsType(typeof(PropertyCondition))) {
					if (parent.IsType(typeof(Trigger)) ||
						parent.IsType(typeof(DataTrigger)) ||
						parent.IsType(typeof(MultiTrigger)) ||
						parent.IsType(typeof(Style)))
					{
						var ttnode = (parent as ElementNode).Properties [new XmlName("", "TargetType")];
						if (ttnode is ValueNode)
							typeName = (ttnode as ValueNode).Value as string;
						else if (ttnode is IElementNode)
							typeName = ((ttnode as IElementNode).CollectionItems.FirstOrDefault() as ValueNode)?.Value as string ?? ((ttnode as IElementNode).Properties [new XmlName("", "TypeName")] as ValueNode)?.Value as string;
					} else if (parent.IsType(typeof(VisualState))) {
						typeName = FindTypeNameForVisualState(parent, node);
					}
				} else if (node.Parent.IsType(typeof(Trigger)))
					typeName = ((node.Parent as ElementNode).Properties [new XmlName("", "TargetType")] as ValueNode).Value as string;
				propertyName = parts [0];
			} else if (parts.Length == 2) {
				typeName = parts [0];
				propertyName = parts [1];
			} else
				throw new XamlParseException($"Cannot convert \"{value}\" into {typeof(BindableProperty)}", node);

			if (typeName == null || propertyName == null)
				throw new XamlParseException($"Cannot convert \"{value}\" into {typeof(BindableProperty)}", node);

			var typeRef = context.TypeResolver.GetManagedType(typeName, node, out _);
			if (typeRef == null)
				throw new XamlParseException($"Can't resolve {typeName}", node);
			bpRef = GetBindablePropertyFieldReference(typeRef, propertyName, module);
			if (bpRef == null)
				throw new XamlParseException($"Can't resolve {propertyName} on {typeRef.Name}", node);
			return bpRef;
		}

		static string FindTypeNameForVisualState(IElementNode parent, IXmlLineInfo lineInfo)
		{
			//1. parent is VisualState, don't check that

			//2. check that the VS is in a VSG
			if (!(parent.Parent is IElementNode target) || 
				!target.IsType(typeof(VisualStateGroup)))
				throw new XamlParseException($"Expected {nameof(VisualStateGroup)} but found {parent.Parent}", lineInfo);

			//3. if the VSG is in a VSGL, skip that as it could be implicit
			if (   target.Parent is ListNode
				|| target.Parent.IsType(typeof(VisualStateGroupList)))
				target = target.Parent.Parent as IElementNode;
			else
				target = target.Parent as IElementNode;

			//4. target is now a Setter in a Style, or a VE
			if (target.IsType(typeof(Setter)))
				return ((target?.Parent as IElementNode)?.Properties[new XmlName("", "TargetType")] as ValueNode)?.Value as string;
			else
				return target.XmlType.Name;
		}

		public static FieldReference GetBindablePropertyFieldReference(TypeReference typeRef, string propertyName, ModuleDefinition module)
		{
			TypeReference declaringTypeReference;
			FieldReference bpRef = typeRef.GetField(fd => fd.Name == $"{propertyName}Property" && fd.IsStatic && fd.IsPublic, out declaringTypeReference);
			if (bpRef != null) {
				bpRef = module.ImportReference(bpRef.ResolveGenericParameters(declaringTypeReference));
				bpRef.FieldType = module.ImportReference(bpRef.FieldType);
			}
			return bpRef;
		}
	}
}