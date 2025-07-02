using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ByteShelf.Extensions;

namespace ByteShelf.Tests
{
    [TestClass]
    public class HttpContextExtensionsTests
    {
        private HttpContext _httpContext = null!;

        [TestInitialize]
        public void Setup()
        {
            _httpContext = new DefaultHttpContext();
        }

        [TestMethod]
        public void GetTenantId_WhenTenantIdExists_ReturnsTenantId()
        {
            // Arrange
            string expectedTenantId = "tenant1";
            _httpContext.Items["TenantId"] = expectedTenantId;

            // Act
            string result = _httpContext.GetTenantId();

            // Assert
            Assert.AreEqual(expectedTenantId, result);
        }

        [TestMethod]
        public void GetTenantId_WhenTenantIdDoesNotExist_ThrowsInvalidOperationException()
        {
            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => _httpContext.GetTenantId());
        }

        [TestMethod]
        public void GetTenantId_WhenTenantIdIsNull_ThrowsInvalidOperationException()
        {
            // Arrange
            _httpContext.Items["TenantId"] = null;

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => _httpContext.GetTenantId());
        }

        [TestMethod]
        public void GetTenantId_WhenTenantIdIsWrongType_ThrowsInvalidOperationException()
        {
            // Arrange
            _httpContext.Items["TenantId"] = 123; // Wrong type

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => _httpContext.GetTenantId());
        }

        [TestMethod]
        public void GetTenantId_WithNullContext_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                HttpContextExtensions.GetTenantId(null!));
        }

        [TestMethod]
        public void IsAdmin_WhenIsAdminExistsAndTrue_ReturnsTrue()
        {
            // Arrange
            _httpContext.Items["IsAdmin"] = true;

            // Act
            bool result = _httpContext.IsAdmin();

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsAdmin_WhenIsAdminExistsAndFalse_ReturnsFalse()
        {
            // Arrange
            _httpContext.Items["IsAdmin"] = false;

            // Act
            bool result = _httpContext.IsAdmin();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsAdmin_WhenIsAdminDoesNotExist_ReturnsFalse()
        {
            // Act
            bool result = _httpContext.IsAdmin();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsAdmin_WhenIsAdminIsNull_ReturnsFalse()
        {
            // Arrange
            _httpContext.Items["IsAdmin"] = null;

            // Act
            bool result = _httpContext.IsAdmin();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsAdmin_WhenIsAdminIsWrongType_ReturnsFalse()
        {
            // Arrange
            _httpContext.Items["IsAdmin"] = "not-a-boolean";

            // Act
            bool result = _httpContext.IsAdmin();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsAdmin_WithNullContext_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                HttpContextExtensions.IsAdmin(null!));
        }

        [TestMethod]
        public void GetTenantId_WithEmptyString_ReturnsEmptyString()
        {
            // Arrange
            _httpContext.Items["TenantId"] = string.Empty;

            // Act
            string result = _httpContext.GetTenantId();

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void GetTenantId_WithWhitespaceString_ReturnsWhitespaceString()
        {
            // Arrange
            _httpContext.Items["TenantId"] = "   ";

            // Act
            string result = _httpContext.GetTenantId();

            // Assert
            Assert.AreEqual("   ", result);
        }

        [TestMethod]
        public void IsAdmin_WithDifferentBooleanValues_ReturnsCorrectValues()
        {
            // Test with true
            _httpContext.Items["IsAdmin"] = true;
            Assert.IsTrue(_httpContext.IsAdmin());

            // Test with false
            _httpContext.Items["IsAdmin"] = false;
            Assert.IsFalse(_httpContext.IsAdmin());

            // Test with missing value
            _httpContext.Items.Remove("IsAdmin");
            Assert.IsFalse(_httpContext.IsAdmin());
        }

        [TestMethod]
        public void GetTenantId_WithDifferentStringValues_ReturnsCorrectValues()
        {
            // Test with regular tenant ID
            _httpContext.Items["TenantId"] = "tenant1";
            Assert.AreEqual("tenant1", _httpContext.GetTenantId());

            // Test with admin tenant ID
            _httpContext.Items["TenantId"] = "admin";
            Assert.AreEqual("admin", _httpContext.GetTenantId());

            // Test with special characters
            _httpContext.Items["TenantId"] = "tenant-with-dashes";
            Assert.AreEqual("tenant-with-dashes", _httpContext.GetTenantId());
        }
    }
}