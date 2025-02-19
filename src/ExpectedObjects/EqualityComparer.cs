﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ExpectedObjects.Strategies;

namespace ExpectedObjects
{
	public class EqualityComparer : IComparisonContext
	{
		readonly IConfiguredContext _configurationContext;
		readonly Stack<string> _stack = new Stack<string>();

		public EqualityComparer(IConfiguredContext configurationContext)
		{
			_configurationContext = configurationContext;
		}

		public bool AreEqual(object expected, object actual, string member)
		{
			try
			{
				bool areEqual = true;
				_stack.Push(member);

				if (ReferenceEquals(expected, actual))
				{
					if (!string.IsNullOrEmpty(member))
					{
						_configurationContext.Writer.Write(new EqualityResult(true, GetMemberPath(), expected, actual));
					}

					return true;
				}

				if (expected == null || actual == null)
				{
					_configurationContext.Writer.Write(new EqualityResult(false, GetMemberPath(), expected, actual));
					return false;
				}

				if (_configurationContext.IgnoreTypes)
				{
					if (actual.GetType() == typeof (MissingMember<>).MakeGenericType(expected.GetType()))
					{
						_configurationContext.Writer.Write(new EqualityResult(false, GetMemberPath(), expected, actual));
						return false;
					}
				}
				else if (!_configurationContext.IgnoreTypes && !expected.GetType().IsAssignableFrom(actual.GetType()))
				{
					_configurationContext.Writer.Write(new EqualityResult(false, GetMemberPath(), expected, actual));
					return false;
				}

				foreach (IComparisonStrategy strategy in _configurationContext.Strategies)
				{
					if (strategy.CanCompare(expected.GetType()))
					{
						areEqual = strategy.AreEqual(expected, actual, this);

						if (!areEqual)
						{
							if (_stack.Count > 0)
							{
								_configurationContext.Writer
									.Write(new EqualityResult(false, GetMemberPath(), expected, actual));
							}
						}

						break;
					}
				}

				return areEqual;
			}
			finally
			{
				if (_stack.Count > 0)
					_stack.Pop();
			}
		}

		public bool CompareProperties(object expected, object actual,
		                              Func<PropertyInfo, PropertyInfo, bool> propertyComparison)
		{
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
			IEnumerable<PropertyInfo> expectedPropertyInfos = expected.GetType().GetProperties(flags)
				.ExcludeHiddenProperties(expected.GetType());
			IEnumerable<PropertyInfo> actualPropertyInfos =
				actual.GetType().GetProperties(flags).ExcludeHiddenProperties(expected.GetType());
			bool areEqual = true;
			expectedPropertyInfos.ToList().ForEach(pi =>
				{
					areEqual = propertyComparison(pi,
					                              actualPropertyInfos
					                              	.Where(p => p.Name.Equals(pi.Name))
					                              	.SingleOrDefault()) & areEqual;
				});

			return areEqual;
		}

		public bool CompareFields(object expected, object actual, Func<FieldInfo, FieldInfo, bool> comparison)
		{
			BindingFlags flags = _configurationContext.GetFieldBindingFlags();
			FieldInfo[] expectedFieldInfos = expected.GetType().GetFields(flags);
			FieldInfo[] actualFieldInfos = actual.GetType().GetFields(flags);
			bool areEqual = true;
			expectedFieldInfos.ToList().ForEach(pi =>
				{
					areEqual = comparison(pi,
					                      actualFieldInfos.Where(p => p.Name.Equals(pi.Name)).SingleOrDefault
					                      	()) & areEqual;
				});

			return areEqual;
		}

		public bool AreEqual(object expected, object actual)
		{
			return AreEqual(expected, actual, (actual != null) ? actual.GetType().Name : string.Empty);
		}

		string GetMemberPath()
		{
			var sb = new StringBuilder();

			sb.Append(String.Join(".", _stack.Reverse().ToArray()));
			sb.Replace(".[", "[");
			return sb.ToString();
		}
	}

	public static class PropertyInfoExtensions
	{
		public static IEnumerable<PropertyInfo> ExcludeHiddenProperties(this IEnumerable<PropertyInfo> infos, Type originType)
		{
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
			IEnumerable<string> newProperties =
				originType.GetProperties(flags).GroupBy(p => p.Name).Where(g => g.Count() > 1).Select(g => g.Key);

			if (newProperties.Count() == 0)
				return infos;

			List<PropertyInfo> properties = infos.Where(p => !newProperties.Contains(p.Name)).ToList();

			foreach (string newProperty in newProperties)
			{
				Type declaringType = originType;
				PropertyInfo closestPropertyDeclaration = null;

				while (closestPropertyDeclaration == null)
				{
					PropertyInfo pi = declaringType.GetProperties(flags)
						.Where(p => p.Name == newProperty)
						.Where(p => p.DeclaringType == declaringType)
						.SingleOrDefault();

					if (pi != null)
						closestPropertyDeclaration = pi;
					else
					{
						declaringType = declaringType.BaseType;
					}
				}

				properties.Add(closestPropertyDeclaration);
			}

			return properties;
		}
	}
}