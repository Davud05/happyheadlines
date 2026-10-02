using HappyHeadlines.Shared.Contracts;

namespace ArticleService.Data;

/// <summary>
/// Example articles inserted into empty databases on first start.
/// Ids are fixed so concurrent instances cannot insert duplicates, and so seeded comments can refer to them.
/// Format: a0000000-0000-0000-0000-{continent number}{article number}, e.g. ...-000008000001 = Global #1.
/// </summary>
public static class SeedArticles
{
    private static readonly Dictionary<string, (string Title, string Content, string Author)[]> Stories = new()
    {
        ["Africa"] =
        [
            ("Solar farms bring power to 2 million homes in Kenya", "New solar parks across the Rift Valley now supply clean electricity to rural villages that never had power before.", "Amara Okoye"),
            ("Mountain gorilla population keeps growing", "Rangers in Rwanda and Uganda report the highest number of mountain gorillas in decades thanks to community-led protection.", "Kofi Mensah"),
        ],
        ["Antarctica"] =
        [
            ("Emperor penguin colony found by satellite", "Researchers spotted a previously unknown colony of thousands of emperor penguins using satellite images.", "Ingrid Larsen"),
            ("Ozone hole continues to heal", "Scientists at the research stations confirm the ozone layer over Antarctica is on track to fully recover.", "Tom Becker"),
        ],
        ["Asia"] =
        [
            ("Tokyo opens world's largest rooftop garden network", "Hundreds of rooftops are now connected green spaces that cool the city and give bees a new home.", "Yuki Tanaka"),
            ("Tigers return to forests in Thailand", "Camera traps show wild tigers breeding in a national park where they had disappeared for years.", "Priya Sharma"),
        ],
        ["Europe"] =
        [
            ("Danish town plants 10,000 trees in one weekend", "Volunteers of all ages turned an old field into a new forest that will capture carbon for generations.", "Mette Hansen"),
            ("Puffins return to the Faroe Islands in record numbers", "Bird watchers counted more breeding puffins this summer than in any year since monitoring began.", "Jonas Petersen"),
        ],
        ["NorthAmerica"] =
        [
            ("Bald eagles thrive across the Great Lakes", "Cleaner water has helped bald eagles return to nesting sites they had abandoned for half a century.", "Sarah Miller"),
            ("Teen invents low-cost water filter", "A 16-year-old from Toronto designed a filter that makes river water safe to drink for less than one dollar.", "Daniel Brooks"),
        ],
        ["Oceania"] =
        [
            ("Great Barrier Reef shows strong coral recovery", "Large parts of the reef recorded the highest coral cover since surveys started almost 40 years ago.", "Olivia Wilson"),
            ("New Zealand predator-free islands welcome kiwi chicks", "Dozens of kiwi chicks hatched on islands cleared of predators, boosting the endangered bird's population.", "Liam Ngata"),
        ],
        ["SouthAmerica"] =
        [
            ("Amazon deforestation falls to lowest level in a decade", "Satellite data shows a sharp drop in forest loss after stronger protection and support for local communities.", "Lucas Silva"),
            ("Andean condors soar again over Patagonia", "Conservationists released rescued condors that are now flying free over the mountains of Argentina.", "Valentina Ruiz"),
        ],
        [Continents.Global] =
        [
            ("Renewables overtake coal in global electricity production", "For the first time, wind and solar together produced more of the world's electricity than coal.", "Ben Carter"),
            ("Child mortality reaches historic low worldwide", "Vaccines and better healthcare mean more children than ever are celebrating their fifth birthday.", "Anna Schmidt"),
            ("Ocean clean-up removes 10 million kilos of plastic", "Clean-up projects in rivers and oceans passed a major milestone, keeping plastic away from marine life.", "Ben Carter"),
        ],
    };

    public static IEnumerable<Article> For(string continent)
    {
        var continentNumber = Continents.All.ToList().IndexOf(continent) + 1;
        var now = DateTimeOffset.UtcNow;

        return Stories[continent].Select((story, i) => new Article
        {
            Id = Guid.Parse($"a0000000-0000-0000-0000-{continentNumber:D6}{i + 1:D6}"),
            Title = story.Title,
            Content = story.Content,
            Author = story.Author,
            Continent = continent,
            PublishedAt = now.AddHours(-(i * 5 + continentNumber)),
        });
    }
}
