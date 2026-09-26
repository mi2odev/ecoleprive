namespace CentreSoutien.Presentation.Core;

/// <summary>Help shown by the "?" button of the header for one page.</summary>
/// <param name="Title">Page name.</param>
/// <param name="Purpose">"À quoi sert cette page" — a few short sentences.</param>
/// <param name="Steps">"Comment faire" — one line per common task.</param>
/// <param name="Shortcuts">Keyboard shortcuts useful on this page ("Ctrl+N : …").</param>
public sealed record PageHelpEntry(string Title, string Purpose, IReadOnlyList<string> Steps, IReadOnlyList<string> Shortcuts)
{
    public bool HasShortcuts => Shortcuts.Count > 0;
}

/// <summary>A keyboard shortcut of the application, listed in the F1 overlay.</summary>
public sealed record ShortcutInfo(string Keys, string Description);

/// <summary>Texts of the page help panels (keyed by <see cref="PageViewModel.NavKey"/>) and the list of keyboard shortcuts.</summary>
public static class PageHelp
{
    /// <summary>Ctrl+1 … Ctrl+9: the nine pages the owner opens most, in this order.</summary>
    public static IReadOnlyList<string> QuickPages { get; } =
        ["dashboard", "students", "payments", "attendance", "schedule", "sessions", "grades", "teachers", "reports"];

    public static IReadOnlyList<ShortcutInfo> Shortcuts { get; } =
    [
        new("F1", "Afficher / masquer cette aide (raccourcis et taille du texte)"),
        new("Ctrl+K", "Rechercher un élève, un parent, un enseignant, un groupe, un document ou un reçu"),
        new("Ctrl+N", "Nouvel élément sur la page ouverte (élève, parent, dépense, séance…)"),
        new("Ctrl+P", "Imprimer la page ouverte (Rapports)"),
        new("F5", "Actualiser la page ouverte"),
        new("Alt+←", "Revenir à la page précédente"),
        new("Ctrl+1", "Tableau de bord"),
        new("Ctrl+2", "Élèves"),
        new("Ctrl+3", "Paiements"),
        new("Ctrl+4", "Présences"),
        new("Ctrl+5", "Emploi du temps"),
        new("Ctrl+6", "Séances"),
        new("Ctrl+7", "Notes"),
        new("Ctrl+8", "Enseignants"),
        new("Ctrl+9", "Rapports"),
        new("Ctrl+L", "Verrouiller l'application"),
        new("Échap", "Fermer la fenêtre ouverte (formulaire, recherche, aide)"),
        new("Entrée", "Valider la recherche ou le formulaire"),
    ];

    private const string New = "Ctrl+N : ";
    private const string Search = "Ctrl+K : rechercher dans tout le centre";
    private const string Refresh = "F5 : actualiser la page";

    public static IReadOnlyDictionary<string, PageHelpEntry> All { get; } = new Dictionary<string, PageHelpEntry>
    {
        ["dashboard"] = new("Tableau de bord",
            "Vue d'ensemble de la journée et du mois : élèves, enseignants, présences, recettes et dépenses. " +
            "La liste « À traiter » regroupe ce qui demande votre attention (impayés, absences répétées, paie des enseignants, appel non fait, sauvegarde en retard). " +
            "Les tendances montrent l'évolution sur 6 mois.",
            [
                "Cliquez sur une ligne de « À traiter » pour aller directement à l'écran concerné.",
                "« Encaisser un paiement » ouvre le formulaire de paiement sans quitter le tableau de bord.",
                "« Faire l'appel » ouvre les présences des séances du jour.",
                "Les raccourcis Élèves, Paiements, Emploi du temps et Rapports mènent aux écrans les plus utilisés.",
            ],
            ["Ctrl+1 : revenir au tableau de bord", Search, Refresh]),

        ["students"] = new("Élèves",
            "Liste de tous les élèves avec leur niveau, leur parent, leurs groupes et le reste à payer. " +
            "Cliquez sur un élève pour ouvrir sa fiche : inscriptions, paiements, notes, présences et documents.",
            [
                "Ajouter : « Ajouter un élève », puis remplissez le nom, le niveau et le parent (existant ou nouveau).",
                "Chercher : tapez un nom, un matricule ou le téléphone du parent ; filtrez par niveau ou par état de paiement.",
                "Encaisser, inscrire dans un groupe, appliquer une remise : ouvrez la fiche de l'élève.",
                "« Importer » ajoute des élèves depuis un fichier Excel (un modèle est proposé) ; « Exporter » crée un fichier Excel de la liste.",
            ],
            [New + "ajouter un élève", "Ctrl+2 : ouvrir les élèves", Search, Refresh]),

        ["parents"] = new("Parents",
            "Les parents et tuteurs, leurs coordonnées, leurs enfants inscrits et le total qu'il leur reste à payer. " +
            "Sélectionnez un parent dans la liste pour voir sa fiche à droite.",
            [
                "Ajouter : « Ajouter un parent », puis nom et téléphone (utilisés pour les relances WhatsApp et SMS).",
                "« Ajouter un enfant » crée un élève déjà rattaché à ce parent.",
                "« Modifier » et « Supprimer » agissent sur le parent sélectionné.",
                "Joignez une pièce (autorisation, pièce d'identité) avec « Ajouter un document ».",
                "« Exporter » crée un fichier Excel des parents.",
            ],
            [New + "ajouter un parent", Search, Refresh]),

        ["teachers"] = new("Enseignants",
            "Liste des enseignants avec leur matière, leurs groupes, leurs élèves et leur mode de rémunération " +
            "(pourcentage, par séance ou forfait mensuel). Cliquez sur un enseignant pour ouvrir sa fiche.",
            [
                "Ajouter : « Ajouter un enseignant », puis matière et règle de rémunération.",
                "Sur la fiche : emploi du temps, groupes, élèves, gains du mois et « Enregistrer un paiement ».",
                "Un enseignant qui ne travaille plus peut être désactivé depuis sa fiche : son historique est conservé.",
                "« Exporter » crée un fichier Excel de la liste.",
            ],
            [New + "ajouter un enseignant", "Ctrl+8 : ouvrir les enseignants", Search, Refresh]),

        ["subjects"] = new("Matières",
            "Les matières enseignées au centre (mathématiques, physique, français…). " +
            "Elles servent à créer les groupes et à classer les enseignants.",
            [
                "Ajouter : « Ajouter une matière », avec un nom et une abréviation courte.",
                "Cliquez sur une matière pour la modifier.",
                "La liste indique combien de groupes et d'enseignants utilisent chaque matière ; « Supprimer » retire une matière inutile.",
            ],
            [New + "ajouter une matière", Refresh]),

        ["groups"] = new("Groupes",
            "Un groupe, c'est une classe : une matière, un niveau, un enseignant, une salle, un horaire et ses élèves " +
            "(ex. « Mathématiques · 3AS A »). Il a un prix pour un paquet de séances (ex. 4 500 DZD pour 4 séances) : l'élève paie " +
            "en rejoignant le groupe, puis à nouveau toutes les 4 séances. Un groupe complet est signalé.",
            [
                "Ajouter : « Nouveau groupe », puis choisissez la matière, le niveau, le paiement (toutes les 4 ou 8 séances, et le prix), l'enseignant, la salle, les jours et les heures.",
                "Cliquez sur un groupe pour ouvrir sa page : prix, enseignant, salle, horaire, élèves, recettes et présences.",
                "Sur la page du groupe, « Nouveau groupe » crée un autre groupe de la même matière et du même niveau (groupe B…).",
                "Pour inscrire un élève : sur la page du groupe (« Ajouter ») ou sur la fiche de l'élève (« Inscrire dans un groupe »).",
                "Les conflits d'horaire (même salle ou même enseignant) sont refusés automatiquement.",
            ],
            [New + "nouveau groupe", Search, Refresh]),

        ["rooms"] = new("Salles",
            "Les salles du centre avec leur capacité, leur équipement et leur occupation dans la semaine. " +
            "Elles servent à planifier les groupes sans double réservation.",
            [
                "Ajouter : « Ajouter une salle », avec son nom et sa capacité.",
                "Cliquez sur une salle pour la modifier.",
                "Le tableau de bord indique les salles libres en ce moment.",
            ],
            [New + "ajouter une salle", Refresh]),

        ["schedule"] = new("Emploi du temps",
            "La semaine du samedi au jeudi avec tous les créneaux des groupes et les séances ponctuelles. " +
            "Filtrez par enseignant ou par salle pour voir un planning précis.",
            [
                "Changez de semaine avec « ← Semaine précédente », « Semaine suivante → » ou « Aujourd'hui ».",
                "Choisissez un enseignant ou une salle dans les filtres pour n'afficher que leurs créneaux.",
                "« Nouvelle séance » ajoute une séance ponctuelle (rattrapage, séance supplémentaire).",
                "L'horaire habituel d'un groupe se change depuis la page du groupe.",
            ],
            ["Ctrl+5 : ouvrir l'emploi du temps", Refresh]),

        ["sessions"] = new("Séances",
            "Les séances réelles de la semaine (date, heure, groupe, salle, enseignant) et leur statut. " +
            "Elles servent à faire l'appel et à calculer la rémunération à la séance.",
            [
                "« Générer depuis l'emploi du temps » crée d'un coup toutes les séances prévues de la semaine.",
                "« Nouvelle séance » ajoute une séance ponctuelle.",
                "« Faire l'appel » ouvre les présences de la séance ; « Modifier » change l'heure ou la salle.",
                "Une séance peut être annulée (elle reste visible) ou supprimée.",
            ],
            [New + "nouvelle séance", "Ctrl+6 : ouvrir les séances", Refresh]),

        ["attendance"] = new("Présences",
            "L'appel des élèves pour chaque séance du jour : présent, absent, en retard ou excusé. " +
            "Les absences comptent dans les alertes du tableau de bord et les bulletins.",
            [
                "Choisissez le jour (← Jour précédent, Jour suivant →, Aujourd'hui) puis la séance à gauche.",
                "« Tous présents » coche tout le monde ; changez ensuite seulement les absents et les retards.",
                "Cliquez sur « Enregistrer » pour garder l'appel.",
                "« Prévenir les parents » prépare les messages d'absence (WhatsApp, SMS ou copie).",
            ],
            ["Ctrl+4 : ouvrir les présences", Refresh]),

        ["grades"] = new("Notes",
            "Saisie des notes par groupe et par évaluation, avec les moyennes pondérées du groupe. " +
            "C'est aussi d'ici que l'on imprime les bulletins.",
            [
                "Choisissez le groupe puis l'évaluation ; tapez les notes dans la colonne, puis « Enregistrer les notes ».",
                "« + Nouvel examen » crée une évaluation (test, devoir, examen) avec son barème et son coefficient.",
                "Un commentaire peut être ajouté pour chaque élève.",
                "« Imprimer les bulletins du groupe » produit un bulletin par élève (impression ou PDF).",
            ],
            ["Ctrl+7 : ouvrir les notes", Refresh]),

        ["exams"] = new("Examens",
            "La liste des évaluations (tests, devoirs, examens) de tous les groupes, avec la date, le barème, le coefficient " +
            "et le nombre de notes déjà saisies.",
            [
                "Ajouter : « Nouvel examen », puis groupe, type, date, note maximale et coefficient.",
                "« Saisir les notes » ouvre directement la feuille de notes de l'évaluation.",
                "« Modifier » ou « Supprimer » sur la ligne de l'évaluation.",
                "Filtrez par groupe ou par type pour retrouver une évaluation.",
            ],
            [New + "nouvel examen", Refresh]),

        ["payments"] = new("Paiements des élèves",
            "Ce que chaque élève reste à payer. Un élève paie en rejoignant un groupe, puis à chaque fin de paquet de séances " +
            "(toutes les 4 ou 8 séances, selon le groupe). " +
            "Les onglets donnent aussi les reçus émis, les remises et les relances aux parents.",
            [
                "Encaisser : « Encaisser un paiement » ou « Encaisser » sur la ligne de l'élève ; le reçu s'imprime.",
                "Onglet « Reçus » : réimprimer ou annuler un reçu.",
                "Onglet « Remises » : créer une remise (fratrie, cas social…), puis l'appliquer depuis la fiche de l'élève.",
                "Onglet « Relances » : messages prêts à envoyer par WhatsApp ou SMS, lettres de relance à imprimer.",
                "« Exporter » crée un fichier Excel de la situation de chaque élève.",
            ],
            ["Ctrl+3 : ouvrir les paiements", Search, Refresh]),

        ["tpayments"] = new("Paiements des enseignants",
            "Ce que chaque enseignant a gagné sur le mois choisi selon sa règle (pourcentage, par séance ou forfait), " +
            "ce qui lui a déjà été versé et ce qui reste à payer.",
            [
                "Choisissez le mois, puis « Payer » sur la ligne de l'enseignant pour enregistrer un versement.",
                "L'historique en bas liste les versements ; « Supprimer » annule un versement saisi par erreur.",
                "La règle de rémunération se change sur la fiche de l'enseignant.",
                "« Exporter » crée un fichier Excel des montants.",
            ],
            [Refresh]),

        ["expenses"] = new("Dépenses",
            "Les dépenses du centre (loyer, électricité, fournitures, entretien…) par catégorie et par mois. " +
            "Elles sont déduites des recettes dans le bénéfice estimé et les rapports.",
            [
                "Ajouter : « Ajouter une dépense », avec la date, la catégorie, le montant et le mode de paiement.",
                "Choisissez le mois et la catégorie pour filtrer la liste.",
                "« Modifier » ou « Supprimer » sur la ligne de la dépense.",
                "« Exporter » crée un fichier Excel du mois affiché.",
            ],
            [New + "ajouter une dépense", Refresh]),

        ["reports"] = new("Rapports",
            "Le bilan du mois choisi : synthèse financière, recettes par groupe et par mode de paiement, élèves avec un reste à payer, " +
            "rémunération des enseignants, dépenses, présences et résultats par groupe.",
            [
                "Choisissez le mois en haut de la page.",
                "« Imprimer / PDF » imprime le rapport complet ou l'enregistre en PDF.",
                "« Exporter tout (Excel) » crée un classeur avec un onglet par tableau.",
            ],
            ["Ctrl+P : imprimer le rapport", "Ctrl+9 : ouvrir les rapports", Refresh]),

        ["documents"] = new("Documents",
            "Tous les fichiers du centre : règlements, contrats, factures, et les pièces jointes aux élèves, enseignants et parents. " +
            "Les fichiers sont copiés dans le dossier des données et inclus dans les sauvegardes.",
            [
                "Ajouter : « Ajouter un document », choisissez le fichier, un titre et une catégorie.",
                "Recherchez par titre, catégorie ou personne.",
                "« Ouvrir » affiche le fichier ; cliquez sur la personne pour ouvrir sa fiche.",
                "Pour joindre une pièce à un élève ou à un enseignant, utilisez aussi le bouton de sa fiche.",
            ],
            [New + "ajouter un document", Search, Refresh]),

        ["settings"] = new("Paramètres",
            "Les réglages du centre : identité et logo, facturation et paiements, remises et rémunérations par défaut, " +
            "présences et notes, reçus, messages aux parents, sauvegardes, langue et thème, sécurité.",
            [
                "Modifiez les champs de la section voulue puis cliquez sur « Enregistrer ».",
                "« Sauvegarder maintenant » crée une sauvegarde chiffrée ; « Restaurer… » revient à une sauvegarde.",
                "Copiez régulièrement les sauvegardes sur une clé USB ou un disque externe.",
                "Les messages aux parents (relances, absences) se personnalisent dans « Messages aux parents ».",
            ],
            [Refresh]),

        ["account"] = new("Mon compte",
            "Le compte unique du propriétaire : nom, coordonnées, photo et mot de passe. " +
            "C'est ce mot de passe qui déverrouille l'application et protège les sauvegardes.",
            [
                "Modifiez votre nom, e-mail ou téléphone puis « Enregistrer ».",
                "« Changer la photo » met à jour l'avatar de la barre latérale et de l'écran de verrouillage.",
                "Pour changer le mot de passe : mot de passe actuel, nouveau mot de passe deux fois, puis « Mettre à jour ».",
                "« Verrouiller » cache l'application sans vous déconnecter.",
            ],
            ["Ctrl+L : verrouiller l'application"]),
    };

    /// <summary>Help of a page, or null for an unknown key.</summary>
    public static PageHelpEntry? For(string? navKey) => navKey is not null && All.TryGetValue(navKey, out var h) ? h : null;
}
