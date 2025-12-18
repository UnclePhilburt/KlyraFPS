using UnityEngine;

[System.Serializable]
public class SoldierIdentity
{
    public string firstName;
    public string lastName;
    public string nickname;
    public string rank;
    public int rankTier; // 1-6 for sorting
    public string callsign;

    // Backstory
    public int age;
    public string hometown;
    public string backstory;
    public string howTheyJoined;
    public string familyInfo;
    public string[] likes;
    public string[] dislikes;
    public string personalQuote;
    public string combatSpecialty;

    // Stats (for display)
    public int kills;
    public int deaths;
    public int missionsCompleted;

    public string FullName => $"{firstName} \"{nickname}\" {lastName}";
    public string RankAndName => $"{rank} {lastName}";

    // Static data for generation
    private static readonly string[] firstNamesPhantom = {
        "James", "Michael", "David", "John", "Robert", "William", "Thomas", "Daniel",
        "Sarah", "Emily", "Jessica", "Ashley", "Amanda", "Samantha", "Nicole", "Maria",
        "Marcus", "Anthony", "Christopher", "Brandon", "Tyler", "Kevin", "Jason", "Ryan"
    };

    private static readonly string[] firstNamesHavoc = {
        "Viktor", "Dmitri", "Alexei", "Nikolai", "Ivan", "Sergei", "Andrei", "Boris",
        "Yuri", "Mikhail", "Pavel", "Oleg", "Roman", "Artem", "Maxim", "Kirill",
        "Natasha", "Katya", "Irina", "Olga", "Svetlana", "Anya", "Elena", "Daria"
    };

    private static readonly string[] lastNamesPhantom = {
        "Walker", "Thompson", "Garcia", "Martinez", "Anderson", "Taylor", "Thomas", "Moore",
        "Jackson", "White", "Harris", "Martin", "Clark", "Lewis", "Robinson", "Young",
        "Mitchell", "Campbell", "Roberts", "Carter", "Phillips", "Evans", "Turner", "Parker"
    };

    private static readonly string[] lastNamesHavoc = {
        "Volkov", "Petrov", "Ivanov", "Sokolov", "Kuznetsov", "Popov", "Vasiliev", "Smirnov",
        "Kozlov", "Novikov", "Morozov", "Fedorov", "Mikhailov", "Orlov", "Andreev", "Belov",
        "Zhukov", "Romanov", "Kovalenko", "Boyko", "Shevchenko", "Tkachenko", "Bondar", "Melnyk"
    };

    private static readonly string[] nicknames = {
        "Ghost", "Viper", "Hawk", "Wolf", "Bear", "Snake", "Eagle", "Raven",
        "Shadow", "Storm", "Frost", "Blaze", "Thunder", "Reaper", "Specter", "Wraith",
        "Bulldog", "Jackal", "Panther", "Cobra", "Scorpion", "Maverick", "Ice", "Steel",
        "Razor", "Switchblade", "Gunner", "Doc", "Sparks", "Wheels", "Joker", "Ace"
    };

    private static readonly string[] ranksPhantom = {
        "Private", "Private First Class", "Corporal", "Sergeant", "Staff Sergeant", "Master Sergeant"
    };

    private static readonly string[] ranksHavoc = {
        "Ryadovoy", "Yefreitor", "Mladshiy Serzhant", "Serzhant", "Starshiy Serzhant", "Starshina"
    };

    private static readonly string[] hometownsPhantom = {
        "Houston, Texas", "Chicago, Illinois", "Phoenix, Arizona", "San Diego, California",
        "Dallas, Texas", "Denver, Colorado", "Seattle, Washington", "Boston, Massachusetts",
        "Atlanta, Georgia", "Miami, Florida", "Detroit, Michigan", "Portland, Oregon",
        "Nashville, Tennessee", "Las Vegas, Nevada", "Salt Lake City, Utah", "Kansas City, Missouri"
    };

    private static readonly string[] hometownsHavoc = {
        "Moscow", "Saint Petersburg", "Novosibirsk", "Yekaterinburg", "Kazan", "Nizhny Novgorod",
        "Chelyabinsk", "Samara", "Omsk", "Rostov-on-Don", "Ufa", "Krasnoyarsk",
        "Voronezh", "Perm", "Volgograd", "Krasnodar"
    };

    private static readonly string[] specialties = {
        "Rifleman", "Marksman", "Heavy Weapons", "Demolitions", "Medic", "Scout",
        "Communications", "Close Quarters", "Anti-Armor", "Designated Marksman"
    };

    private static readonly string[] likesOptions = {
        "classic rock music", "playing chess", "working on cars", "hiking", "fishing",
        "reading history books", "cooking", "playing guitar", "watching football", "photography",
        "woodworking", "card games", "weightlifting", "running", "video games",
        "collecting coins", "hunting", "camping", "martial arts", "writing letters home"
    };

    private static readonly string[] dislikesOptions = {
        "cold weather", "MREs", "waiting around", "paperwork", "early mornings",
        "snakes", "heights", "enclosed spaces", "loud chewing", "bad coffee",
        "politicians", "sand", "humidity", "being told what to do", "running out of ammo",
        "incompetent officers", "mud", "mosquitoes", "silence", "surprise inspections"
    };

    private static readonly string[] quotes = {
        "Stay low, move fast.",
        "The only easy day was yesterday.",
        "Pain is temporary, glory is forever.",
        "I didn't choose this life, but I'll finish it.",
        "Everyone has a plan until they get shot at.",
        "We don't rise to the occasion, we fall to our training.",
        "The more you sweat in training, the less you bleed in combat.",
        "I'm not here to be liked. I'm here to win.",
        "Keep your head down and your powder dry.",
        "Fortune favors the bold.",
        "I've got a family to get home to.",
        "Just point me at the enemy.",
        "Fear is a choice. I choose violence.",
        "I do my talking with my rifle.",
        "No mission too difficult, no sacrifice too great."
    };

    // Backstory templates
    private static readonly string[] backstoryTemplates = {
        "{0} grew up in {1} in a {2} family. {3} After {4}, {5} decided to enlist. {6}",
        "Born and raised in {1}, {0} always knew {7} would end up in the military. {3} {6}",
        "{0} from {1} had a {2} childhood. {3} When {4}, it changed everything. {5} joined up the next month. {6}",
        "The streets of {1} shaped {0} into who {7} is today. {3} {6}",
        "{0} left {1} at eighteen with nothing but {8}. {3} The military became {9} new family. {6}"
    };

    private static readonly string[] familyTypes = {
        "working-class", "military", "large", "small", "broken", "tight-knit", "religious", "immigrant"
    };

    private static readonly string[] childhoodEvents = {
        "Spent summers at grandpa's farm learning to shoot.",
        "Was always getting into fights at school.",
        "Excelled at sports but struggled academically.",
        "Was the quiet kid who surprised everyone.",
        "Worked multiple jobs to help support the family.",
        "Lost a parent young, grew up fast.",
        "Was raised by a single mother who worked two jobs.",
        "Had to be the responsible one among siblings."
    };

    private static readonly string[] joinReasons = {
        "the towers fell on 9/11",
        "a close friend was killed overseas",
        "there were no jobs back home",
        "the family business went under",
        "wanting to escape a dead-end life",
        "following in a parent's footsteps",
        "a recruiter made it sound like adventure",
        "needing money for college"
    };

    private static readonly string[] currentSituations = {
        "Now on {0} third deployment, {1} just wants to get the job done and go home.",
        "{1} has become one of the most reliable soldiers in the unit.",
        "Despite everything, {1} still believes in the mission.",
        "{1} doesn't talk much about the past anymore.",
        "The squad has become {0} real family now.",
        "{1} counts down the days until rotation.",
        "{1} has grown harder with each deployment.",
        "Somehow, {1} still finds reasons to smile."
    };

    private static readonly string[] familyDetails = {
        "Married with two kids back home. Keeps their photos in {0} helmet.",
        "Single, but writes to {0} mother every week without fail.",
        "Engaged before deployment. The wedding is planned for next spring.",
        "Divorced. It's one of {0} biggest regrets.",
        "Has a daughter {1} has never met in person. She was born during the last deployment.",
        "Only child. Parents are getting older and {1} worries about them.",
        "Comes from a big family - four siblings who all served.",
        "Lost touch with family years ago. The squad is all {1} has.",
        "Has a dog named {2} waiting back home. Talks about that dog constantly.",
        "Taking care of younger siblings since their parents passed."
    };

    private static readonly string[] dogNames = {
        "Max", "Duke", "Rocky", "Bear", "Tucker", "Charlie", "Cooper", "Buddy", "Zeus", "Jack"
    };

    public static SoldierIdentity Generate(Team team)
    {
        SoldierIdentity id = new SoldierIdentity();

        bool isPhantom = team == Team.Phantom;
        string[] firstNames = isPhantom ? firstNamesPhantom : firstNamesHavoc;
        string[] lastNames = isPhantom ? lastNamesPhantom : lastNamesHavoc;
        string[] ranks = isPhantom ? ranksPhantom : ranksHavoc;
        string[] hometowns = isPhantom ? hometownsPhantom : hometownsHavoc;

        id.firstName = firstNames[Random.Range(0, firstNames.Length)];
        id.lastName = lastNames[Random.Range(0, lastNames.Length)];
        id.nickname = nicknames[Random.Range(0, nicknames.Length)];

        id.rankTier = Random.Range(1, 7);
        id.rank = ranks[Mathf.Clamp(id.rankTier - 1, 0, ranks.Length - 1)];

        id.callsign = $"{id.nickname}-{Random.Range(1, 10)}{Random.Range(1, 10)}";
        id.age = Random.Range(19, 42);
        id.hometown = hometowns[Random.Range(0, hometowns.Length)];
        id.combatSpecialty = specialties[Random.Range(0, specialties.Length)];

        // Generate likes/dislikes
        id.likes = new string[3];
        id.dislikes = new string[3];
        var usedLikes = new System.Collections.Generic.HashSet<int>();
        var usedDislikes = new System.Collections.Generic.HashSet<int>();

        for (int i = 0; i < 3; i++)
        {
            int likeIdx;
            do { likeIdx = Random.Range(0, likesOptions.Length); } while (usedLikes.Contains(likeIdx));
            usedLikes.Add(likeIdx);
            id.likes[i] = likesOptions[likeIdx];

            int dislikeIdx;
            do { dislikeIdx = Random.Range(0, dislikesOptions.Length); } while (usedDislikes.Contains(dislikeIdx));
            usedDislikes.Add(dislikeIdx);
            id.dislikes[i] = dislikesOptions[dislikeIdx];
        }

        id.personalQuote = quotes[Random.Range(0, quotes.Length)];

        // Generate backstory
        id.backstory = GenerateBackstory(id, isPhantom);
        id.familyInfo = GenerateFamilyInfo(id);
        id.howTheyJoined = joinReasons[Random.Range(0, joinReasons.Length)];

        // Random stats
        id.kills = Random.Range(3, 47);
        id.deaths = Random.Range(0, 4);
        id.missionsCompleted = Random.Range(5, 25);

        return id;
    }

    private static string GenerateBackstory(SoldierIdentity id, bool isPhantom)
    {
        string template = backstoryTemplates[Random.Range(0, backstoryTemplates.Length)];
        string familyType = familyTypes[Random.Range(0, familyTypes.Length)];
        string childhood = childhoodEvents[Random.Range(0, childhoodEvents.Length)];
        string joinReason = joinReasons[Random.Range(0, joinReasons.Length)];

        string pronoun = IsFeminineName(id.firstName) ? "she" : "he";
        string possessive = IsFeminineName(id.firstName) ? "her" : "his";
        string objective = IsFeminineName(id.firstName) ? "her" : "him";

        string currentSit = currentSituations[Random.Range(0, currentSituations.Length)];
        currentSit = string.Format(currentSit, possessive, id.firstName);

        string randomItem = new string[] {
            "a duffel bag and a dream",
            "twenty dollars and determination",
            "nothing but the clothes on " + possessive + " back",
            "a photo of " + possessive + " family"
        }[Random.Range(0, 4)];

        return string.Format(template,
            id.firstName,           // {0}
            id.hometown,            // {1}
            familyType,             // {2}
            childhood,              // {3}
            joinReason,             // {4}
            pronoun,                // {5}
            currentSit,             // {6}
            pronoun,                // {7}
            randomItem,             // {8}
            possessive              // {9}
        );
    }

    private static string GenerateFamilyInfo(SoldierIdentity id)
    {
        string template = familyDetails[Random.Range(0, familyDetails.Length)];
        string possessive = IsFeminineName(id.firstName) ? "her" : "his";
        string pronoun = IsFeminineName(id.firstName) ? "she" : "he";
        string dogName = dogNames[Random.Range(0, dogNames.Length)];

        return string.Format(template, possessive, pronoun, dogName);
    }

    private static bool IsFeminineName(string name)
    {
        string[] feminineNames = {
            "Sarah", "Emily", "Jessica", "Ashley", "Amanda", "Samantha", "Nicole", "Maria",
            "Natasha", "Katya", "Irina", "Olga", "Svetlana", "Anya", "Elena", "Daria"
        };
        return System.Array.Exists(feminineNames, n => n == name);
    }
}
