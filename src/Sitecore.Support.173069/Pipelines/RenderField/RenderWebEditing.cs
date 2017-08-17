namespace Sitecore.Support.Pipelines.RenderField
{
    using Sitecore;
    using Sitecore.Configuration;
    using Sitecore.Data;
    using Sitecore.Data.Fields;
    using Sitecore.Data.Items;
    using Sitecore.Diagnostics;
    using Sitecore.Globalization;
    using Sitecore.Pipelines.GetChromeData;
    using Sitecore.Pipelines.RenderField;
    using Sitecore.Shell;
    using Sitecore.Sites;
    using Sitecore.StringExtensions;
    using Sitecore.Text;
    using Sitecore.Web;
    using Sitecore.Web.UI.HtmlControls;
    using Sitecore.Web.UI.PageModes;
    using Sitecore.Xml.Xsl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Web;
    using System.Web.UI;

    public class RenderWebEditing
    {
        private void AddParameters(Tag tag, RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(tag, "tag");
            Assert.ArgumentNotNull(args, "args");
            if (args.WebEditParameters.Count > 0)
            {
                UrlString str = new UrlString();
                foreach (System.Collections.Generic.KeyValuePair<string, string> pair in args.WebEditParameters)
                {
                    str.Add(pair.Key, pair.Value);
                }
                tag.Add("sc_parameters", str.ToString());
            }
        }

        private static void ApplyWordFieldStyle(Tag tag, RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(tag, "tag");
            Assert.ArgumentNotNull(args, "args");
            string str = args.Parameters["editorwidth"] ?? Settings.WordOCX.Width;
            string str2 = args.Parameters["editorheight"] ?? Settings.WordOCX.Height;
            string str3 = args.Parameters["editorpadding"] ?? Settings.WordOCX.Padding;
            str = str.ToLowerInvariant().Replace("px", string.Empty);
            int @int = MainUtil.GetInt(str, -1);
            str2 = str2.ToLowerInvariant().Replace("px", string.Empty);
            int num2 = MainUtil.GetInt(str2, -1);
            int num3 = MainUtil.GetInt(str3.ToLowerInvariant().Replace("px", string.Empty), -1);
            if (num3 >= 0)
            {
                if (@int >= 0)
                {
                    str = (@int + (2 * num3)) + string.Empty;
                }
                if (num2 >= 0)
                {
                    str2 = (num2 + (2 * num3)) + string.Empty;
                }
            }
            tag.Class = tag.Class + " scWordContainer";
            tag.Style = "width:{0}px;height:{1}px;padding:{2};".FormatWith(new object[] { str, str2, str3 });
        }

        private bool CanEditField(Field field)
        {
            Assert.ArgumentNotNull(field, "field");
            if (!field.CanWrite)
            {
                return false;
            }
            return true;
        }

        private bool CanEditItem(Item item)
        {
            Assert.ArgumentNotNull(item, "item");
            if ((!Context.IsAdministrator && item.Locking.IsLocked()) && !item.Locking.HasLock())
            {
                return false;
            }
            if (!item.Access.CanWrite())
            {
                return false;
            }
            if (!item.Access.CanWriteLanguage())
            {
                return false;
            }
            if (item.Appearance.ReadOnly)
            {
                return false;
            }
            return true;
        }

        private bool CanWebEdit(RenderFieldArgs args)
        {
            if (args.DisableWebEdit)
            {
                return false;
            }
            SiteContext site = Context.Site;
            if (site == null)
            {
                return false;
            }
            if (site.DisplayMode != DisplayMode.Edit)
            {
                return false;
            }
            if (WebUtil.GetQueryString("sc_duration") == "temporary")
            {
                return false;
            }
            if (!Context.PageMode.IsPageEditorEditing)
            {
                return false;
            }
            return true;
        }

        private static Tag CreateFieldTag(string tagName, RenderFieldArgs args, string controlID)
        {
            Assert.ArgumentNotNull(tagName, "tagName");
            Assert.ArgumentNotNull(args, "args");
            Tag tag = new Tag(tagName)
            {
                ID = controlID + "_edit"
            };
            tag.Add("scFieldType", args.FieldTypeKey);
            return tag;
        }

        private static string GetDefaultText(RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            string str = StringUtil.GetString((string[])new string[] { args.RenderParameters["default-text"], string.Empty });
            using (new LanguageSwitcher(WebUtil.GetCookieValue("shell", "lang", Context.Language.Name)))
            {
                if (str.IsNullOrEmpty())
                {
                    Database database = Factory.GetDatabase("core");
                    Assert.IsNotNull(database, "core");
                    Item item = database.GetItem("/sitecore/content/Applications/WebEdit/WebEdit Texts");
                    Assert.IsNotNull(item, "/sitecore/content/Applications/WebEdit/WebEdit Texts");
                    str = item["Default Text"];
                }
                if (string.Compare(args.RenderParameters["show-title-when-blank"], "true", System.StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    str = GetFieldDisplayName(args) + ": " + str;
                }
            }
            return str;
        }

        private string GetEditableElementTagName(RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            string str = "span";
            if ((UIUtil.IsFirefox() || UIUtil.IsWebkit()) && (UIUtil.SupportsInlineEditing() && MainUtil.GetBool(args.Parameters["block-content"], false)))
            {
                str = "div";
            }
            return str;
        }

        private static string GetFieldData(RenderFieldArgs args, Field field, string controlID)
        {
            Assert.ArgumentNotNull(args, "args");
            Assert.ArgumentNotNull(field, "field");
            Assert.ArgumentNotNull(controlID, "controlID");
            Item item = field.Item;
            Assert.IsNotNull(Context.Site, "site");
            using (new LanguageSwitcher(WebUtil.GetCookieValue("shell", "lang", Context.Site.Language)))
            {
                GetChromeDataArgs args2 = new GetChromeDataArgs("field", item, args.Parameters);
                args2.CustomData["field"] = field;
                GetChromeDataPipeline.Run(args2);
                ChromeData chromeData = args2.ChromeData;
                SetCommandParametersValue(chromeData.Commands, field, controlID);
                return chromeData.ToJson();
            }
        }

        private static string GetFieldDisplayName(RenderFieldArgs args)
        {
            Item item;
            Assert.IsNotNull(args, "args");
            Assert.IsNotNull(args.Item, "item");
            if (string.Compare(WebUtil.GetCookieValue("shell", "lang", Context.Language.Name), args.Item.Language.Name, System.StringComparison.InvariantCultureIgnoreCase) != 0)
            {
                item = args.Item.Database.GetItem(args.Item.ID);
                Assert.IsNotNull(item, "Item");
            }
            else
            {
                item = args.Item;
            }
            Field field = item.Fields[args.FieldName];
            if (field != null)
            {
                return field.DisplayName;
            }
            return args.FieldName;
        }

        private string GetRawValueContainer(Field field, string controlID)
        {
            Assert.ArgumentNotNull(field, "field");
            Assert.ArgumentNotNull(controlID, "controlID");
            return "<input id='{0}' class='scFieldValue' name='{0}' type='hidden' value=\"{1}\" />".FormatWith(new object[] { controlID, HttpUtility.HtmlEncode(field.Value) });
        }

        public void Process(RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (((this.CanWebEdit(args) || args.WebEditParameters.ContainsKey("sc-highlight-contentchange")) && (args.Item != null)) && this.CanEditItem(args.Item))
            {
                Field field = args.Item.Fields[args.FieldName];
                if ((field != null) && this.CanEditField(field))
                {
                    Item item = field.Item;
                    string str = item[FieldIDs.Revision].Replace("-", string.Empty);
                    string controlID = string.Concat((object[])new object[] { "fld_", item.ID.ToShortID(), "_", field.ID.ToShortID(), "_", item.Language, "_", item.Version, "_", str, "_", ((int)MainUtil.GetSequencer()) });
                    HtmlTextWriter output = new HtmlTextWriter(new System.IO.StringWriter());
                    string rawValueContainer = this.GetRawValueContainer(field, controlID);
                    output.Write(rawValueContainer);
                    if (args.DisableWebEditContentEditing && args.DisableWebEditFieldWrapping)
                    {
                        this.RenderWrapperlessField(output, args, field, controlID);
                    }
                    else
                    {
                        this.RenderWrappedField(output, args, field, controlID);
                    }
                }
            }
        }

        private void RenderWrappedField(HtmlTextWriter output, RenderFieldArgs args, Field field, string controlID)
        {
            Assert.ArgumentNotNull(output, "output");
            Assert.ArgumentNotNull(args, "args");
            Assert.ArgumentNotNull(controlID, "controlID");
            string str = GetFieldData(args, field, controlID);
            output.Write("<div style=\"display: inline\"><span class=\"scChromeData\">{0}</span>", str);
            Tag tag = CreateFieldTag(this.GetEditableElementTagName(args), args, controlID);
            tag.Class = "scWebEditInput";
            if (!args.DisableWebEditContentEditing)
            {
                tag.Add("contenteditable", "true");
            }
            string firstPart = args.Result.FirstPart;
            if (string.IsNullOrEmpty(firstPart))
            {
                tag.Add("scWatermark", "true");
                firstPart = GetDefaultText(args);
            }
            this.AddParameters(tag, args);
            if ((args.FieldTypeKey.ToLowerInvariant() == "word document") && (args.Parameters["editormode"] == "inline"))
            {
                ApplyWordFieldStyle(tag, args);
            }
            output.Write(tag.Start());
            output.Write(firstPart);
            args.Result.FirstPart = output.InnerWriter.ToString();
            RenderFieldResult result = args.Result;
            result.LastPart = result.LastPart + tag.End() + "</div>";
        }

        private void RenderWrapperlessField(HtmlTextWriter output, RenderFieldArgs args, Field field, string controlID)
        {
            Assert.ArgumentNotNull(output, "output");
            Assert.ArgumentNotNull(args, "args");
            Assert.ArgumentNotNull(controlID, "controlID");
            Tag tag = CreateFieldTag("code", args, controlID);
            tag.Class = "scpm";
            tag.Add("kind", "open").Add("type", "text/sitecore").Add("chromeType", "field");
            string firstPart = args.Result.FirstPart;
            if (string.IsNullOrEmpty(firstPart))
            {
                tag.Add("scWatermark", "true");
                string defaultText = GetDefaultText(args);
                firstPart = defaultText;
                if (StringUtil.RemoveTags(defaultText) == defaultText)
                {
                    firstPart = "<span class='scTextWrapper'>" + defaultText + "</span>";
                }
            }
            this.AddParameters(tag, args);
            string str3 = GetFieldData(args, field, controlID);
            tag.InnerHtml = str3;
            output.Write(tag.ToString());
            output.Write(firstPart);
            args.Result.FirstPart = output.InnerWriter.ToString();
            Tag tag2 = new Tag("code")
            {
                Class = "scpm"
            };
            tag2.Add("kind", "close").Add("type", "text/sitecore").Add("chromeType", "field");
            RenderFieldResult result = args.Result;
            result.LastPart = result.LastPart + tag2.ToString();
        }

        private static void SetCommandParametersValue(System.Collections.Generic.IEnumerable<WebEditButton> commands, Field field, string controlID)
        {
            string str;
            Assert.ArgumentNotNull(commands, "commands");
            Assert.ArgumentNotNull(field, "field");
            Assert.ArgumentNotNull(controlID, "controlID");
            Item item = field.Item;
            if (UserOptions.WebEdit.UsePopupContentEditor)
            {
                str = string.Concat((object[])new object[] { "javascript:Sitecore.WebEdit.postRequest(\"webedit:edit(id=", item.ID, ",language=", item.Language, ",version=", item.Version, ")\")" });
            }
            else
            {
                UrlString str2 = new UrlString(WebUtil.GetRawUrl());
                str2["sc_ce"] = "1";
                str2["sc_ce_uri"] = HttpUtility.UrlEncode(item.Uri.ToString());
                str = str2.ToString();
            }
            foreach (WebEditButton button in commands)
            {
                if (!string.IsNullOrEmpty(button.Click))
                {
                    string str3 = button.Click.Replace("$URL", str).Replace("$ItemID", item.ID.ToString()).Replace("$Language", item.Language.ToString()).Replace("$Version", item.Version.ToString()).Replace("$FieldID", field.ID.ToString()).Replace("$ControlID", controlID).Replace("$MessageParameters", string.Concat((object[])new object[] { "itemid=", item.ID, ",language=", item.Language, ",version=", item.Version, ",fieldid=", field.ID, ",controlid=", controlID })).Replace("$JavascriptParameters", string.Concat((object[])new object[] { "\"", item.ID, "\",\"", item.Language, "\",\"", item.Version, "\",\"", field.ID, "\",\"", controlID, "\"" }));
                    button.Click = str3;
                }
            }
        }
    }
}
