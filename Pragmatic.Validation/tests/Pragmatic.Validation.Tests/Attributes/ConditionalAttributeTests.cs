using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class ConditionalAttributeTests
{
    private enum PaymentMethod
    {
        Cash,
        CreditCard,
        BankTransfer
    }

    private class PaymentModel : IPropertyValueProvider
    {
        public PaymentMethod Method { get; set; }
        public string? CardNumber { get; set; }
        public string? BankAccount { get; set; }

        public object? GetPropertyValue(string propertyName) => propertyName switch
        {
            nameof(Method) => Method,
            nameof(CardNumber) => CardNumber,
            nameof(BankAccount) => BankAccount,
            _ => null
        };
    }

    public class RequiredIfAttributeTests
    {
        [Fact]
        public void RequiresInstance_ReturnsTrue()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard);
            attr.RequiresInstance.Should().BeTrue();
        }

        [Fact]
        public void IsValid_ConditionNotMet_ValueNull_ReturnsTrue()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard);
            var model = new PaymentModel { Method = PaymentMethod.Cash, CardNumber = null };
            attr.IsValid(null, model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ConditionMet_ValueNull_ReturnsFalse()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard);
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = null };
            attr.IsValid(null, model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ConditionMet_ValueEmpty_ReturnsFalse()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard);
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = "" };
            attr.IsValid("", model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ConditionMet_ValueProvided_ReturnsTrue()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard);
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = "4111111111111111" };
            attr.IsValid("4111111111111111", model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ConditionMet_AllowEmptyStrings_ReturnsTrue()
        {
            var attr = new RequiredIfAttribute(nameof(PaymentModel.Method), PaymentMethod.CreditCard)
            {
                AllowEmptyStrings = true
            };
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = "" };
            attr.IsValid("", model).Should().BeTrue();
        }
    }

    public class RequiredIfNotAttributeTests
    {
        [Fact]
        public void RequiresInstance_ReturnsTrue()
        {
            var attr = new RequiredIfNotAttribute(nameof(PaymentModel.Method), PaymentMethod.Cash);
            attr.RequiresInstance.Should().BeTrue();
        }

        [Fact]
        public void IsValid_ConditionNotMet_ValueNull_ReturnsTrue()
        {
            // Not required when Method IS Cash
            var attr = new RequiredIfNotAttribute(nameof(PaymentModel.Method), PaymentMethod.Cash);
            var model = new PaymentModel { Method = PaymentMethod.Cash, CardNumber = null };
            attr.IsValid(null, model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ConditionMet_ValueNull_ReturnsFalse()
        {
            // Required when Method is NOT Cash
            var attr = new RequiredIfNotAttribute(nameof(PaymentModel.Method), PaymentMethod.Cash);
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = null };
            attr.IsValid(null, model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ConditionMet_ValueProvided_ReturnsTrue()
        {
            var attr = new RequiredIfNotAttribute(nameof(PaymentModel.Method), PaymentMethod.Cash);
            var model = new PaymentModel { Method = PaymentMethod.CreditCard, CardNumber = "4111111111111111" };
            attr.IsValid("4111111111111111", model).Should().BeTrue();
        }
    }
}