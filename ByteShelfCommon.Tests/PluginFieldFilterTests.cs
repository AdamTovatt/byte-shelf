namespace ByteShelfCommon.Tests
{
    public class PluginFieldFilterTests
    {
        private static readonly DateTimeOffset NewYear = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static TheoryData<string, PluginFieldCondition, string> Written => new TheoryData<string, PluginFieldCondition, string>
        {
            { "p.f", new PluginFieldCondition.Has(true), "p.f" },
            { "!p.f", new PluginFieldCondition.Has(false), "!p.f" },
            { "p.f~a=b..c~d", new PluginFieldCondition.Contains("a=b..c~d"), "p.f~a=b..c~d" },
            { "p.f=1..5.5", new PluginFieldCondition.Between(1, 5.5), "p.f=1..5.5" },
            { "p.f=-5", new PluginFieldCondition.Between(-5, -5), "p.f=-5..-5" },
            { "p.f=..5", new PluginFieldCondition.Between(null, 5), "p.f=..5" },
            { "p.f=1e3..", new PluginFieldCondition.Between(1000, null), "p.f=1000.." },
            { "p.f=2026-01-01..2026-01-02T01:00+01:00", new PluginFieldCondition.During(NewYear, NewYear.AddDays(1)), "p.f=2026-01-01T00:00:00Z..2026-01-02T00:00:00Z" },
            { "p.f=..2026-01-01", new PluginFieldCondition.During(null, NewYear), "p.f=..2026-01-01T00:00:00Z" },
            { "p.f=2026-01-01T00:00:00.0000001Z..", new PluginFieldCondition.During(NewYear.AddTicks(1), null), "p.f=2026-01-01T00:00:00.0000001Z.." },
            { "p.f=true", new PluginFieldCondition.Is(true), "p.f=true" },
            { "p.f=false", new PluginFieldCondition.Is(false), "p.f=false" },
        };

        [Theory]
        [MemberData(nameof(Written))]
        public void Parse_AFilter_IsReadAndWrittenBack(string text, PluginFieldCondition condition, string writtenBack)
        {
            PluginFieldFilter filter = PluginFieldFilter.Parse(text);

            Assert.Equal(("p", "f", condition), (filter.Plugin, filter.Field, filter.Condition));
            Assert.Equal(writtenBack, filter.ToString());
            Assert.Equal(filter, PluginFieldFilter.Parse(writtenBack));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("p")]
        [InlineData(".f")]
        [InlineData("p.")]
        [InlineData("p.f.g")]
        [InlineData("!p.f=1")]
        [InlineData("p.f=")]
        [InlineData("p.f=..")]
        [InlineData("p.f=one")]
        [InlineData("p.f=1..2026-01-01")]
        [InlineData("p.f=2026-01-01")]
        [InlineData("p.f=2026-01-01T10:00:00..")]
        [InlineData("p.f=True")]
        [InlineData("p.f=NaN")]
        [InlineData("p.f=1e999")]
        public void TryParse_SomethingElse_IsNone(string? text)
        {
            Assert.False(PluginFieldFilter.TryParse(text, out PluginFieldFilter? filter));
            Assert.Null(filter);
        }

        public static TheoryData<PluginFieldCondition> Refused => new TheoryData<PluginFieldCondition>
        {
            new PluginFieldCondition.Contains(" "),
            new PluginFieldCondition.Between(null, null),
            new PluginFieldCondition.Between(2, 1),
            new PluginFieldCondition.Between(double.NaN, null),
            new PluginFieldCondition.Between(null, double.PositiveInfinity),
            new PluginFieldCondition.During(null, null),
            new PluginFieldCondition.During(NewYear.AddDays(1), NewYear),
            new PluginFieldCondition.During(NewYear, NewYear),
        };

        [Theory]
        [MemberData(nameof(Refused))]
        public void Normalize_ConditionThatIsNotAllowed_IsRefused(PluginFieldCondition condition)
        {
            FileSearch search = Search(new PluginFieldFilter("p", "f", condition));

            Assert.Contains("p.f", Assert.Throws<ArgumentException>(() => search.Normalize()).Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Normalize_Text_IsTrimmedAndFolded()
        {
            FileSearch search = Search(PluginFieldFilter.Parse("p.f~ ÅSA ")).Normalize();

            Assert.Equal(new PluginFieldCondition.Contains("åsa"), Assert.Single(search.PluginFields).Condition);
        }

        [Fact]
        public void Normalize_MoreThan20Filters_IsRefused()
        {
            PluginFieldFilter filter = PluginFieldFilter.Parse("p.f");

            Assert.Equal(20, Search(Enumerable.Repeat(filter, 20).ToArray()).Normalize().PluginFields.Count);
            Assert.Throws<ArgumentException>(() => Search(Enumerable.Repeat(filter, 21).ToArray()).Normalize());
        }

        [Fact]
        public void Normalize_FilterThatIsMissing_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => Search(PluginFieldFilter.Parse("p.f"), null!).Normalize());
        }

        [Fact]
        public void ToQuery_Filters_AreEachAParameter()
        {
            FileSearch search = Search(PluginFieldFilter.Parse("p.f=1..2"), PluginFieldFilter.Parse("p.g~a&b"));

            Assert.Equal("pluginFields=p.f%3D1..2&pluginFields=p.g~a%26b", search.ToQuery());
            Assert.Equal(search.PluginFields, new DuplicateSearch(search).PluginFields);
        }

        public static TheoryData<PluginFieldCondition, PluginFieldValue?, bool> Conditions { get; } = new TheoryData<PluginFieldCondition, PluginFieldValue?, bool>
        {
            { new PluginFieldCondition.Has(true), null, false },
            { new PluginFieldCondition.Has(false), null, true },
            { new PluginFieldCondition.Between(3, 3), PluginFieldValue.OfNumber(3), true },
            { new PluginFieldCondition.Between(3.1, null), PluginFieldValue.OfNumber(3), false },
            { new PluginFieldCondition.Between(null, 2.9), PluginFieldValue.OfNumber(3), false },
            { new PluginFieldCondition.During(NewYear, null), PluginFieldValue.OfDate(NewYear), true },
            { new PluginFieldCondition.During(null, NewYear), PluginFieldValue.OfDate(NewYear), false },
            { new PluginFieldCondition.Is(false), null, false },
            { new PluginFieldCondition.Contains("åsa"), PluginFieldValue.OfText("Hej Åsa"), true },
        };

        [Theory]
        [MemberData(nameof(Conditions))]
        public void Matches_Condition_DecidesByTheValue(PluginFieldCondition condition, PluginFieldValue? value, bool matches)
        {
            Assert.Equal(matches, condition.Matches(value));
        }

        [Theory]
        [InlineData("q.f", "There is no plugin 'q' here; the plugins here are: p.")]
        [InlineData("p.g", "The plugin 'p' declares no field 'g'; it declares: f, n.")]
        [InlineData("p.f=1..2", "p.f is a text, which cannot be compared as a number.")]
        [InlineData("p.n~x", "p.n is a number, which cannot be compared as a text.")]
        public void Check_FilterOnWhatIsNotDeclared_IsRefusedNamingWhatIs(string filter, string message)
        {
            InstalledPlugins plugins = new InstalledPlugins([new PluginDeclaration(
                "p",
                "P",
                [new PluginField("f", "F", PluginFieldKind.Text, PluginDocumentSubject.File), new PluginField("n", "N", PluginFieldKind.Number, PluginDocumentSubject.File)])]);

            Assert.Equal(message, Assert.Throws<ArgumentException>(() => plugins.Check([PluginFieldFilter.Parse("p.f"), PluginFieldFilter.Parse(filter)])).Message);
        }

        [Fact]
        public void Check_FiltersOnWhatIsDeclared_AnswersEachWithItsPluginAndField()
        {
            PluginField field = new PluginField("f", "F", PluginFieldKind.Text, PluginDocumentSubject.File);
            PluginDeclaration plugin = new PluginDeclaration("p", "P", [field]);
            PluginFieldFilter filter = PluginFieldFilter.Parse("p.f");

            Assert.Equal([(filter, plugin, field)], new InstalledPlugins([new PluginDeclaration("q", "Q", [field]), plugin]).Check([filter]));
        }

        private static FileSearch Search(params PluginFieldFilter[] filters)
        {
            return new FileSearch { PluginFields = filters };
        }
    }
}
