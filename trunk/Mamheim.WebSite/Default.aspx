<%@ Page Title="Home Page" Language="C#" MasterPageFile="~/Site.master" AutoEventWireup="true"
    CodeFile="Default.aspx.cs" Inherits="_Default" %>

<asp:Content ID="HeaderContent" runat="server" ContentPlaceHolderID="HeadContent">
</asp:Content>
<asp:Content ID="BodyContent" runat="server" ContentPlaceHolderID="MainContent">
    <h2>
        Welcome to ASP.NET!
    </h2>
    <button>Button</button>
    <br />
			<form style="margin-top: 1em;">
				<div id="radioset">
					<input type="radio" id="radio1" name="radio" /><label for="radio1">Choice 1</label>
					<input type="radio" id="radio2" name="radio" checked="checked" /><label for="radio2">Choice 2</label>
					<input type="radio" id="radio3" name="radio" /><label for="radio3">Choice 3</label>
				</div>
			</form>
    <br />
    <asp:TextBox ID="txtStartDate" ClientIDMode="Static" runat="server" />  
    <p>
        To learn more about ASP.NET visit <a href="http://www.asp.net" title="ASP.NET Website">www.asp.net</a>.
    </p>
    <p>
        You can also find <a href="http://go.microsoft.com/fwlink/?LinkID=152368&amp;clcid=0x409"
            title="MSDN ASP.NET Docs">documentation on ASP.NET at MSDN</a>.
    </p>
</asp:Content>
