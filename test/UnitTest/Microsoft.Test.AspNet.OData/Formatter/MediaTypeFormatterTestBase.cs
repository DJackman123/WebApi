﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.OData.Common;
using Microsoft.Test.AspNet.OData.TestCommon;
using Microsoft.Test.AspNet.OData.TestCommon.DataTypes;
using Moq;
using Xunit;

namespace Microsoft.Test.AspNet.OData.Formatter
{
    /// <summary>
    /// A test class for common <see cref="MediaTypeFormatter"/> functionality across multiple implementations.
    /// </summary>
    /// <typeparam name="TFormatter">The type of formatter under test.</typeparam>
    public abstract class MediaTypeFormatterTestBase<TFormatter> where TFormatter : MediaTypeFormatter
    {
        protected MediaTypeFormatterTestBase()
        {
        }

        // Test data variations of interest in round-trip tests.
        public const TestDataVariations RoundTripDataVariations =
            TestDataVariations.All | TestDataVariations.WithNull | TestDataVariations.AsClassMember;

        public abstract IEnumerable<MediaTypeHeaderValue> ExpectedSupportedMediaTypes { get; }

        public abstract IEnumerable<Encoding> ExpectedSupportedEncodings { get; }

        /// <summary>
        /// Byte representation of an <see cref="SampleType"/> with value 42 using the default encoding
        /// for this media type formatter.
        /// </summary>
        public abstract byte[] ExpectedSampleTypeByteRepresentation { get; }

        [Fact]
        public void TypeIsCorrect()
        {
            TypeAssert.HasProperties<TFormatter, MediaTypeFormatter>(TypeAssert.TypeProperties.IsPublicVisibleClass);
        }

        [Fact]
        public void SupportedMediaTypes_HeaderValuesAreNotSharedBetweenInstances()
        {
            var formatter1 = CreateFormatter();
            var formatter2 = CreateFormatter();

            foreach (MediaTypeHeaderValue mediaType1 in formatter1.SupportedMediaTypes)
            {
                MediaTypeHeaderValue mediaType2 = formatter2.SupportedMediaTypes.Single(m => m.Equals(mediaType1));
                Assert.NotSame(mediaType1, mediaType2);
            }
        }

        [Fact]
        public void SupportEncodings_ValuesAreNotSharedBetweenInstances()
        {
            var formatter1 = CreateFormatter();
            var formatter2 = CreateFormatter();

            foreach (Encoding mediaType1 in formatter1.SupportedEncodings)
            {
                Encoding mediaType2 = formatter2.SupportedEncodings.Single(m => m.Equals(mediaType1));
                Assert.NotSame(mediaType1, mediaType2);
            }
        }

        [Fact]
        public void SupportMediaTypes_DefaultSupportedMediaTypes()
        {
            TFormatter formatter = CreateFormatter();
            Assert.True(ExpectedSupportedMediaTypes.SequenceEqual(formatter.SupportedMediaTypes));
        }

        [Fact]
        public void SupportEncoding_DefaultSupportedEncodings()
        {
            TFormatter formatter = CreateFormatter();
            Assert.True(ExpectedSupportedEncodings.SequenceEqual(formatter.SupportedEncodings));
        }

        [Fact]
        public void ReadFromStreamAsync_ThrowsOnNull()
        {
            TFormatter formatter = CreateFormatter();
            ExceptionAssert.ThrowsArgumentNull(() => { formatter.ReadFromStreamAsync(null, Stream.Null, null, null); }, "type");
            ExceptionAssert.ThrowsArgumentNull(() => { formatter.ReadFromStreamAsync(typeof(object), null, null, null); }, "readStream");
        }

        [Fact]
        public Task ReadFromStreamAsync_WhenContentLengthIsZero_DoesNotReadStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            Mock<Stream> mockStream = new Mock<Stream>();
            IFormatterLogger mockFormatterLogger = new Mock<IFormatterLogger>().Object;
            HttpContent content = new StringContent(String.Empty);
            HttpContentHeaders contentHeaders = content.Headers;
            contentHeaders.ContentLength = 0;

            // Act 
            return formatter.ReadFromStreamAsync(typeof(SampleType), mockStream.Object, content, mockFormatterLogger)
                .ContinueWith(
                    readTask =>
                    {
                        // Assert
                        Assert.Equal(TaskStatus.RanToCompletion, readTask.Status);
                        mockStream.Verify(s => s.Read(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
                        mockStream.Verify(s => s.ReadByte(), Times.Never());
                        mockStream.Verify(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(), It.IsAny<object>()), Times.Never());
                    });
        }

        [Fact]
        public Task ReadFromStreamAsync_WhenContentLengthIsZero_DoesNotCloseStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            Mock<Stream> mockStream = new Mock<Stream>();
            IFormatterLogger mockFormatterLogger = new Mock<IFormatterLogger>().Object;
            HttpContent content = new StringContent(String.Empty);
            HttpContentHeaders contentHeaders = content.Headers;
            contentHeaders.ContentLength = 0;

            // Act 
            return formatter.ReadFromStreamAsync(typeof(SampleType), mockStream.Object, content, mockFormatterLogger)
                .ContinueWith(
                    readTask =>
                    {
                        // Assert
                        Assert.Equal(TaskStatus.RanToCompletion, readTask.Status);
                        mockStream.Verify(s => s.Close(), Times.Never());
                    });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(0)]
        [InlineData("")]
        public async Task ReadFromStreamAsync_WhenContentLengthIsZero_ReturnsDefaultTypeValue<T>(T value)
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            HttpContent content = new StringContent("");

            // Act
            T result = (T)await formatter.ReadFromStreamAsync(typeof(T), await content.ReadAsStreamAsync(),
                content, null);

            // Assert
            Assert.NotNull(value.GetType());
            Assert.Equal(default(T), result);
        }

        [Fact]
        public Task ReadFromStreamAsync_ReadsDataButDoesNotCloseStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            MemoryStream memStream = new MemoryStream(ExpectedSampleTypeByteRepresentation);
            HttpContent content = new StringContent(String.Empty);
            HttpContentHeaders contentHeaders = content.Headers;
            contentHeaders.ContentLength = memStream.Length;
            contentHeaders.ContentType = CreateSupportedMediaType();

            // Act
            return formatter.ReadFromStreamAsync(typeof(SampleType), memStream, content, null).ContinueWith(
                async readTask =>
                {
                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, readTask.Status);
                    Assert.True(memStream.CanRead);

                    var value = Assert.IsType<SampleType>(await readTask);
                    Assert.Equal(42, value.Number);
                });
        }

        [Fact]
        public Task ReadFromStreamAsync_WhenContentLengthIsNull_ReadsDataButDoesNotCloseStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            MemoryStream memStream = new MemoryStream(ExpectedSampleTypeByteRepresentation);
            HttpContent content = new StringContent(String.Empty);
            HttpContentHeaders contentHeaders = content.Headers;
            contentHeaders.ContentLength = null;
            contentHeaders.ContentType = CreateSupportedMediaType();

            // Act
            return formatter.ReadFromStreamAsync(typeof(SampleType), memStream, content, null).ContinueWith(
                async readTask =>
                {
                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, readTask.Status);
                    Assert.True(memStream.CanRead);

                    var value = Assert.IsType<SampleType>(await readTask);
                    Assert.Equal(42, value.Number);
                });
        }

        [Fact]
        public void WriteToStreamAsync_ThrowsOnNull()
        {
            TFormatter formatter = CreateFormatter();
            ExceptionAssert.ThrowsArgumentNull(() => { formatter.WriteToStreamAsync(null, new object(), Stream.Null, null, null); }, "type");
            ExceptionAssert.ThrowsArgumentNull(() => { formatter.WriteToStreamAsync(typeof(object), new object(), null, null, null); }, "writeStream");
        }

        [Fact]
        public virtual Task WriteToStreamAsync_WhenObjectIsNull_WritesDataButDoesNotCloseStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            Mock<Stream> mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.CanWrite).Returns(true);
            HttpContent content = new StringContent(String.Empty);

            // Act 
            return formatter.WriteToStreamAsync(typeof(SampleType), null, mockStream.Object, content, null).ContinueWith(
                writeTask =>
                {
                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, writeTask.Status);
                    mockStream.Verify(s => s.Close(), Times.Never());
                    mockStream.Verify(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(), It.IsAny<object>()), Times.Never());
                });
        }

        [Fact]
        public Task WriteToStreamAsync_WritesDataButDoesNotCloseStream()
        {
            // Arrange
            TFormatter formatter = CreateFormatter();
            SampleType sampleType = new SampleType { Number = 42 };
            MemoryStream memStream = new MemoryStream();
            HttpContent content = new StringContent(String.Empty);
            content.Headers.ContentType = CreateSupportedMediaType();

            // Act
            return formatter.WriteToStreamAsync(typeof(SampleType), sampleType, memStream, content, null).ContinueWith(
                writeTask =>
                {
                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, writeTask.Status);
                    Assert.True(memStream.CanRead);

                    byte[] actualSampleTypeByteRepresentation = memStream.ToArray();
                    Assert.NotEmpty(actualSampleTypeByteRepresentation);
                });
        }

        [Fact]
        public virtual async Task Overridden_WriteToStreamAsyncWithoutCancellationToken_GetsCalled()
        {
            // Arrange
            Stream stream = new MemoryStream();
            Mock<TFormatter> formatter = CreateMockFormatter();
            ObjectContent<int> content = new ObjectContent<int>(42, formatter.Object);

            formatter
                .Setup(f => f.WriteToStreamAsync(typeof(int), 42, stream, content, null /* transportContext */))
                .Returns(TaskHelpers.Completed())
                .Verifiable();

            // Act
            await content.CopyToAsync(stream);

            // Assert
            formatter.Verify();
        }

        [Fact]
        public virtual async Task Overridden_WriteToStreamAsyncWithCancellationToken_GetsCalled()
        {
            // Arrange
            Stream stream = new MemoryStream();
            Mock<TFormatter> formatter = CreateMockFormatter();
            ObjectContent<int> content = new ObjectContent<int>(42, formatter.Object);

            formatter
                .Setup(f => f.WriteToStreamAsync(typeof(int), 42, stream, content, null /* transportContext */, CancellationToken.None))
                .Returns(TaskHelpers.Completed())
                .Verifiable();

            // Act
            await content.CopyToAsync(stream);

            // Assert
            formatter.Verify();
        }

        [Fact]
        public virtual async Task Overridden_ReadFromStreamAsyncWithoutCancellationToken_GetsCalled()
        {
            // Arrange
            Stream stream = new MemoryStream();
            Mock<TFormatter> formatter = CreateMockFormatter();
            formatter.Object.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/test"));
            StringContent content = new StringContent(" ", Encoding.Default, "application/test");
            CancellationTokenSource cts = new CancellationTokenSource();

            formatter
                .Setup(f => f.ReadFromStreamAsync(typeof(string), It.IsAny<Stream>(), content, null /*formatterLogger */))
                .Returns(Task.FromResult<object>(null))
                .Verifiable();

            // Act
            await content.ReadAsAsync<string>(new[] { formatter.Object }, cts.Token);

            // Assert
            formatter.Verify();
        }

        [Fact]
        public virtual async Task Overridden_ReadFromStreamAsyncWithCancellationToken_GetsCalled()
        {
            // Arrange
            Stream stream = new MemoryStream();
            Mock<TFormatter> formatter = CreateMockFormatter();
            formatter.Object.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/test"));
            StringContent content = new StringContent(" ", Encoding.Default, "application/test");
            CancellationTokenSource cts = new CancellationTokenSource();

            formatter
                .Setup(f => f.ReadFromStreamAsync(typeof(string), It.IsAny<Stream>(), content, null /*formatterLogger */, cts.Token))
                .Returns(Task.FromResult<object>(null))
                .Verifiable();

            // Act
            await content.ReadAsAsync<string>(new[] { formatter.Object }, cts.Token);

            // Assert
            formatter.Verify();
        }

        protected virtual TFormatter CreateFormatter()
        {
            ConstructorInfo constructor = typeof(TFormatter).GetConstructor(Type.EmptyTypes);
            return (TFormatter)constructor.Invoke(null);
        }

        protected virtual Mock<TFormatter> CreateMockFormatter()
        {
            return new Mock<TFormatter>() { CallBase = true };
        }

        protected virtual MediaTypeHeaderValue CreateSupportedMediaType()
        {
            return ExpectedSupportedMediaTypes.First();
        }

        public static Encoding CreateOrGetSupportedEncoding(MediaTypeFormatter formatter, string encoding, bool isDefaultEncoding)
        {
            Encoding enc = null;
            if (isDefaultEncoding)
            {
                enc = formatter.SupportedEncodings.First((e) => e.WebName.Equals(encoding, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                enc = Encoding.GetEncoding(encoding);
                formatter.SupportedEncodings.Add(enc);
            }

            return enc;
        }

        protected static Task ReadContentUsingCorrectCharacterEncodingHelper(MediaTypeFormatter formatter, string content, string formattedContent, string mediaType, string encoding, bool isDefaultEncoding)
        {
            // Arrange
            Encoding enc = CreateOrGetSupportedEncoding(formatter, encoding, isDefaultEncoding);
            byte[] sourceData = enc.GetBytes(formattedContent);

            // Further Arrange, Act & Assert
            return ReadContentusingCorrectCharacterEncodingHelper(formatter, content, sourceData, mediaType);
        }

        protected static Task ReadContentusingCorrectCharacterEncodingHelper(MediaTypeFormatter formatter, string content, byte[] sourceData, string mediaType)
        {
            // Arrange
            MemoryStream memStream = new MemoryStream(sourceData);

            StringContent dummyContent = new StringContent(string.Empty);
            HttpContentHeaders headers = dummyContent.Headers;
            headers.Clear();
            headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            headers.ContentLength = sourceData.Length;

            IFormatterLogger mockFormatterLogger = new Mock<IFormatterLogger>().Object;

            // Act & Assert
            return formatter.ReadFromStreamAsync(typeof(string), memStream, dummyContent, mockFormatterLogger).ContinueWith(
                async (readTask) =>
                {
                    string result = (await readTask) as string;

                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, readTask.Status);
                    Assert.Equal(content, result);
                });
        }

        protected static Task WriteContentUsingCorrectCharacterEncodingHelper(MediaTypeFormatter formatter, string content, string formattedContent, string mediaType, string encoding, bool isDefaultEncoding)
        {
            // Arrange
            Encoding enc = CreateOrGetSupportedEncoding(formatter, encoding, isDefaultEncoding);

            byte[] preamble = enc.GetPreamble();
            byte[] data = enc.GetBytes(formattedContent);
            byte[] expectedData = new byte[preamble.Length + data.Length];
            Buffer.BlockCopy(preamble, 0, expectedData, 0, preamble.Length);
            Buffer.BlockCopy(data, 0, expectedData, preamble.Length, data.Length);

            // Further Arrange, Act & Assert
            return WriteContentusingCorrectCharacterEncodingHelper(formatter, content, expectedData, mediaType);
        }

        protected static Task WriteContentusingCorrectCharacterEncodingHelper(MediaTypeFormatter formatter, string content, byte[] expectedData, string mediaType)
        {
            // Arrange
            MemoryStream memStream = new MemoryStream();

            StringContent dummyContent = new StringContent(string.Empty);
            HttpContentHeaders headers = dummyContent.Headers;
            headers.Clear();
            headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            headers.ContentLength = expectedData.Length;

            IFormatterLogger mockFormatterLogger = new Mock<IFormatterLogger>().Object;

            // Act & Assert
            return formatter.WriteToStreamAsync(typeof(string), content, memStream, dummyContent, null).ContinueWith(
                (writeTask) =>
                {
                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, writeTask.Status);
                    byte[] actualData = memStream.ToArray();

                    Assert.Equal(expectedData, actualData);
                });
        }
    }

    [DataContract(Name = "DataContractSampleType")]
    public class SampleType
    {
        [DataMember]
        public int Number { get; set; }
    }
}