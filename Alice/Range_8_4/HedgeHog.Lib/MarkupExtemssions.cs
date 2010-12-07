using System;
using System.Windows.Markup;
using System.Collections;

namespace HedgeHog.Markup {
  [MarkupExtensionReturnType(typeof(IEnumerable))]

  public class EnumValuesExtension : MarkupExtension {

    public EnumValuesExtension() {

    }

    public EnumValuesExtension(Type enumType) {

      this.EnumType = enumType;

    }

    [ConstructorArgument("enumType")]

    public Type EnumType { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) {

      if (this.EnumType == null)

        throw new ArgumentException("The enum type is not set");

      return Enum.GetValues(this.EnumType);

    }

  }

  //It can be used like that :

  //<ListBox Name="myComboBox" SelectedIndex="0" Margin="8"

  //              ItemsSource="{my:EnumValues HorizontalAlignment}"/>

}
