﻿// october 15, 2015 | soren granfeldt
//	- added transform RegexIsMatch
// october 15, 2015 | soren granfeldt
//	- added MultiValueConcatenate and MultiValueRemoveIfNotMatch
//	- change type of data flowing through transforms from string to object to support multivalues

using Microsoft.MetadirectoryServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace FIM.MARE
{
	[
		XmlInclude(typeof(ToUpper)),
		XmlInclude(typeof(ToLower)),
		XmlInclude(typeof(Trim)),
		XmlInclude(typeof(TrimEnd)),
		XmlInclude(typeof(TrimStart)),
		XmlInclude(typeof(Replace)),
		XmlInclude(typeof(PadLeft)),
		XmlInclude(typeof(PadRight)),
		XmlInclude(typeof(RegexReplace)),
		XmlInclude(typeof(Substring)),
		XmlInclude(typeof(RegexSelect)),
		XmlInclude(typeof(RegexIsMatch)),
		XmlInclude(typeof(FormatDate)),
		XmlInclude(typeof(Base64ToGUID)),
		XmlInclude(typeof(IsBitSet)),
		XmlInclude(typeof(IsBitNotSet)),
		XmlInclude(typeof(SIDToString)),
		XmlInclude(typeof(SetBit)),
		XmlInclude(typeof(LookupMVValue)),
		XmlInclude(typeof(MultiValueConcatenate)),
		XmlInclude(typeof(MultiValueRemoveIfNotMatch))
	]
	public abstract class Transform
	{
		public abstract object Convert(object value);

		protected List<object> FromValueCollection(object value)
		{
			List<object> values = new List<object>();
			if (value.GetType() == typeof(List<object>))
			{
				Tracer.TraceInformation("already-type-list");
				values = (List<object>)value;
			}
			else
			{
				Tracer.TraceInformation("converting-to-list");
				ValueCollection vc = (ValueCollection)value;
				foreach (Microsoft.MetadirectoryServices.Value val in vc)
				{
					values.Add(val.ToString());
				}
			}
			return values;
		}
	}

	#region multivalues
	public class MultiValueConcatenate : Transform
	{
		[XmlAttribute("Separator")]
		public string Separator { get; set; }
		public override object Convert(object value)
		{
			if (value == null) return value;

			string returnValue = null;
			List<object> values = FromValueCollection(value);
			foreach (object val in values)
			{
				Tracer.TraceInformation("source-value {0}", val.ToString());
				returnValue = returnValue + val.ToString() + this.Separator;
			}
			returnValue = returnValue.Substring(0, returnValue.LastIndexOf(this.Separator));
			return returnValue;
		}
	}
	public class MultiValueRemoveIfNotMatch : Transform
	{
		[XmlAttribute("Pattern")]
		public string Pattern { get; set; }
		public override object Convert(object value)
		{
			if (value == null) return value;

			List<object> values = FromValueCollection(value);
			List<object> returnValues = new List<object>();
			foreach (object val in values)
			{
				if (Regex.IsMatch(val.ToString(), this.Pattern, RegexOptions.IgnoreCase))
				{
					Tracer.TraceInformation("removing-value {0}", val);
				}
				else
				{
					Tracer.TraceInformation("keeping-value {0}", val.ToString());
					returnValues.Add(val);
				}
			}
			return returnValues;
		}
	}

	#endregion

	public class Base64ToGUID : Transform
	{
		public override object Convert(object value)
		{
			if (value == null) return value;
			Guid guid = new Guid(System.Convert.FromBase64String(value as string));
			return guid;
		}
	}

	public enum SecurityIdentifierType
	{
		[XmlEnum(Name = "AccountSid")]
		AccountSid,
		[XmlEnum(Name = "AccountDomainSid")]
		AccountDomainSid
	}
	public class SIDToString : Transform
	{
		[XmlAttribute("SIDType")]
		[XmlTextAttribute()]
		public SecurityIdentifierType SIDType { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			var sidInBytes = System.Convert.FromBase64String(value as string);
			var sid = new SecurityIdentifier(sidInBytes, 0);
			value = SIDType.Equals(SecurityIdentifierType.AccountSid) ? sid.Value : sid.AccountDomainSid.Value;
			return value;
		}
	}

	public class LookupMVValue : Transform
	{
		[XmlAttribute("LookupAttributeName")]
		public string LookupAttributeName { get; set; }

		[XmlAttribute("ExtractValueFromAttribute")]
		public string ExtractValueFromAttribute { get; set; }

		[XmlAttribute("MAName")]
		public string MAName { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			MVEntry mventry = Utils.FindMVEntries(LookupAttributeName, value as string, 1).FirstOrDefault();
			if (mventry != null)
			{
				if (this.ExtractValueFromAttribute.Equals("[DN]"))
				{
					ConnectorCollection col = mventry.ConnectedMAs[MAName].Connectors;
					if (col != null && col.Count.Equals(1))
					{
						value = mventry.ConnectedMAs[MAName].Connectors.ByIndex[0].DN.ToString();
					}
				}
				else
				{
					value = mventry[ExtractValueFromAttribute].IsPresent ? mventry[ExtractValueFromAttribute].Value : null;
				}
			}
			return value;
		}
	}

	public class IsBitSet : Transform
	{
		[XmlAttribute("BitPosition")]
		public int BitPosition { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			long longValue = long.Parse(value as string);
			value = ((longValue & (1 << this.BitPosition)) != 0).ToString();
			return value;
		}
	}
	public class IsBitNotSet : Transform
	{
		[XmlAttribute("BitPosition")]
		public int BitPosition { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			long longValue = long.Parse(value as string);
			value = ((longValue & (1 << this.BitPosition)) == 0).ToString();
			return value;
		}
	}
	public class SetBit : Transform
	{
		[XmlAttribute("BitPosition")]
		public int BitPosition { get; set; }

		[XmlAttribute("Value")]
		public bool Value { get; set; }

		private int SetBitAt(int value, int index)
		{
			if (index < 0 || index >= sizeof(long) * 8)
			{
				throw new ArgumentOutOfRangeException();
			}

			return value | (1 << index);
		}
		private int UnsetBitAt(int value, int index)
		{
			if (index < 0 || index >= sizeof(int) * 8)
			{
				throw new ArgumentOutOfRangeException();
			}

			return value & ~(1 << index);
		}
		public override object Convert(object value)
		{
			if (value == null) return value;
			int val = int.Parse(value as string);
			val = this.Value ? SetBitAt(val, BitPosition) : UnsetBitAt(val, BitPosition);
			return val.ToString();
		}
	}
	public class ToUpper : Transform
	{
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().ToUpper();
		}
	}
	public class ToLower : Transform
	{
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().ToLower();
		}
	}
	public class Trim : Transform
	{
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().Trim();
		}
	}
	public class TrimEnd : Transform
	{
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().TrimEnd();
		}
	}
	public class TrimStart : Transform
	{
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().TrimStart();
		}
	}
	public class Replace : Transform
	{
		[XmlAttribute("OldValue")]
		public string OldValue { get; set; }
		[XmlAttribute("NewValue")]
		public string NewValue { get; set; }
		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().Replace(OldValue, NewValue);
		}
	}
	public class PadLeft : Transform
	{
		[XmlAttribute("TotalWidth")]
		public int TotalWidth { get; set; }
		[XmlAttribute("PaddingChar")]
		public string PaddingChar { get; set; }

		public override object Convert(object value)
		{
			PaddingChar = string.IsNullOrEmpty(PaddingChar) ? " " : PaddingChar;
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().PadLeft(TotalWidth, PaddingChar[0]);
		}
	}
	public class PadRight : Transform
	{
		[XmlAttribute("TotalWidth")]
		public int TotalWidth { get; set; }
		[XmlAttribute("PaddingChar")]
		public string PaddingChar { get; set; }

		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : value.ToString().PadRight(TotalWidth, PaddingChar[0]);
		}
	}
	public class RegexReplace : Transform
	{
		[XmlAttribute("Pattern")]
		public string Pattern { get; set; }
		[XmlAttribute("Replacement")]
		public string Replacement { get; set; }

		public override object Convert(object value)
		{
			return string.IsNullOrEmpty(value as string) ? value : Regex.Replace(value as string, this.Pattern, this.Replacement);
		}
	}
	public class Substring : Transform
	{
		[XmlAttribute("StartIndex")]
		public int StartIndex { get; set; }
		[XmlAttribute("Length")]
		public int Length { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			string val = value as string;
			return val.Length <= StartIndex ? "" : val.Length - StartIndex <= Length ? val.Substring(StartIndex) : val.Substring(StartIndex, Length);
		}
	}
	public class RegexIsMatch : Transform
	{
		[XmlAttribute("Pattern")]
		public string Pattern { get; set; }

		[XmlAttribute("TrueValue")]
		public string TrueValue { get; set; }

		[XmlAttribute("FalseValue")]
		public string FalseValue { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return FalseValue;
			return Regex.IsMatch(value as string, Pattern, RegexOptions.IgnoreCase) ? TrueValue : FalseValue;
		}
	}
	public class RegexSelect : Transform
	{
		public override object Convert(object value)
		{
			throw new NotImplementedException();
		}
	}
	public enum DateType
	{
		[XmlEnum(Name = "BestGuess")]
		BestGuess,
		[XmlEnum(Name = "DateTime")]
		DateTime,
		[XmlEnum(Name = "FileTimeUTC")]
		FileTimeUTC
	}
	public class FormatDate : Transform
	{
		[XmlAttribute("DateType")]
		[XmlTextAttribute()]
		public DateType DateType { get; set; }

		[XmlAttribute("FromFormat")]
		public string FromFormat { get; set; }
		[XmlAttribute("ToFormat")]
		public string ToFormat { get; set; }

		public override object Convert(object value)
		{
			if (value == null) return value;
			string returnValue = value as string;
			if (DateType.Equals(DateType.FileTimeUTC))
			{
				returnValue = DateTime.FromFileTimeUtc(long.Parse(value as string)).ToString(ToFormat);
				return returnValue;
			}
			if (DateType.Equals(DateType.BestGuess))
			{
				returnValue = DateTime.Parse(value as string, CultureInfo.InvariantCulture).ToString(ToFormat);
				return returnValue;
			}
			if (DateType.Equals(DateType.DateTime))
			{
				returnValue = DateTime.ParseExact(value as string, FromFormat, CultureInfo.InvariantCulture).ToString(ToFormat);
				return returnValue;
			}
			return returnValue;
		}
	}
	public class Transforms
	{
		[XmlElement("Transform")]
		public List<Transform> Transform { get; set; }
	}

}
