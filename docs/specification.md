# Clarity · Spécification produit et technique

Version 2.0 · Spécification de référence pour le développement

> Clarity donne aux consultants une vue complète de l’environnement client pour préparer leurs migrations Microsoft 365 et Active Directory.

## 1. Produit, utilisateurs et résultat attendu

**Clarity** est une application web interne multi-client destinée aux consultants en migration. **Discovery** est la fonctionnalité de collecte et d’inventaire. Microsoft Graph est son collecteur par défaut pour Microsoft 365. Les consultants approfondissent l’inventaire avec des scripts PowerShell fournis par Discovery, exécutés dans leur environnement autorisé. Ils déposent ensuite le paquet standard dans Clarity. Les exports administratifs pris en charge et les constats des consultants rejoignent le même inventaire, avec leur provenance.

- **Tenant-to-tenant**. Découvrir la source, inventorier la cible lorsqu’elle est disponible, rapprocher les objets et identifier les écarts qui affectent la préparation.
- **Carve-out**. Définir la population qui part, inventorier ses dépendances et identifier les ressources également utilisées par les personnes qui restent.
- **Carve-in**. Inventorier l’environnement entrant et la cible existante, mettre en évidence les doublons, les collisions et les dépendances à conserver ou à adapter.
- **Audit seul**. Ouvrir un dossier avec une source, publier la synthèse et ajouter une cible plus tard si la mission évolue.

Le produit couvre la découverte, la consolidation, la comparaison, la revue du périmètre et les exports. Les actions dans les tenants et l’exécution des migrations sont réalisées dans les outils d’administration et de migration prévus pour la mission. L’IA sert à développer Clarity. Le produit applique des collectes, rapprochements et analyses reproductibles.

Le résultat est un **dossier d’inventaire consultable et actualisable**. Un consultant doit pouvoir reprendre le travail d’un collègue, identifier ce qui a été collecté, retrouver la preuve d’un constat et refaire un export après actualisation sans ressaisir ses décisions.

## 2. Organisation des dossiers et des accès

| Objet | Règle |
|---|---|
| **Espace client** | Frontière d’accès aux dossiers, inventaires, fichiers et connexions. Un consultant peut être membre de plusieurs espaces. |
| **Dossier** | Mission nommée, scénario, description, statut actif ou archivé et membres autorisés via l’espace. Le périmètre et les décisions appartiennent au dossier. |
| **Environnement** | Tenant Microsoft 365, forêt AD, périmètre Azure ou autre périmètre d’inventaire déclaré. Le dossier lui attribue un rôle source ou cible. |
| **Connexion** | Autorisation Graph en lecture liée au client et au tenant réel. Les scripts utilisent une connexion ouverte localement par le consultant. Clarity conserve uniquement les métadonnées utiles à la collecte. |
| **Collecte** | Exécution Graph ou import daté pour un dossier, un environnement et un périmètre. Les résultats sont publiés par section. Une demande de script peut rester en attente d’un fichier. |
| **Source seule** | Le dossier reste utilisable. Une cible prévue peut recevoir des hypothèses, comme son domaine cible, avant d’être connectée. |

Le modèle permet plusieurs environnements de chaque côté. L’interface propose un chemin simple avec un tenant M365 source, une ou plusieurs sources complémentaires dont l’AD, puis une cible facultative. Un tenant retrouvé dans deux espaces clients ne crée aucun lien transversal.

**Rôles recommandés**. Le gestionnaire d’espace administre ses membres et connexions. Le consultant collecte, importe, qualifie et exporte. Le lecteur consulte et télécharge les livrables auxquels son rôle donne accès. L’administrateur de la plateforme gère les espaces et l’exploitation. L’accès aux inventaires passe par une attribution explicite. Commencer avec les droits par espace et prévoir les restrictions par dossier lorsque l’organisation en a besoin. Le contrôle s’applique au serveur pour chaque lecture, modification, téléchargement et lancement de job.

## 3. Interface et parcours

Navigation de l’application. **Clients → Dossiers → Vue d’ensemble · Discovery · Inventaire · Périmètre · Comparaison · Exports**. L’en-tête de dossier affiche le client et le côté actif. Les états vides proposent l’action pertinente, comme ajouter une source, lancer une collecte ou importer un paquet.

| Écran | Contenu et actions |
|---|---|
| **Clients et dossiers** | Rechercher, créer et archiver. Afficher le dernier inventaire, ses sections à vérifier et la présence d’une cible. |
| **Vue d’ensemble** | Résumé de la population et des volumes, inventaires datés, couverture par domaine, derniers changements et points à vérifier. Chaque indicateur ouvre sa liste filtrée. |
| **Discovery · Connexions** | Déclarer un environnement, tester les accès Graph par section, afficher les permissions manquantes et les collectes PowerShell disponibles. Réutiliser une connexion Graph autorisée dans son périmètre. |
| **Discovery · Collectes** | Lancer ou relancer des sections Graph. Pour une collecte PowerShell, télécharger le script et ses paramètres, puis déposer le ZIP. Déposer un export administratif si un profil est disponible. Afficher un aperçu avant intégration, puis les résultats et les sections à reprendre. |
| **Discovery · Couverture** | Montrer les sections prévues, leur provenance, leur périmètre réel et la date de la dernière observation valable. Indiquer l’action requise selon le cas, comme relancer Graph, télécharger le script ou déposer un fichier. Donner accès à l’historique par section. |
| **Inventaire** | Recherche par nom, UPN, SMTP, URL ou identifiant technique. Filtres par environnement, domaine, site, type, statut et collecte. Grilles paginées avec tri serveur et colonnes adaptées au type d’objet. |
| **Fiche d’objet** | En-tête d’identité, propriétés essentielles, relations navigables, observations et historique. Les valeurs, liens et constats donnent accès à leur source. |
| **Périmètre** | Constituer une population par groupe, OU, domaine, site, filtre ou liste importée. Inclure, exclure ou laisser à arbitrer en lot. Repérer les dépendances et les ressources partagées entre populations. Lot de migration facultatif. |
| **Comparaison** | Deux modes. Comparer deux collectes d’un environnement ou comparer source et cible. Montrer les changements, collisions, objets absents, inconnus ou ambigus et les décisions à revoir. |
| **Exports** | Générer une synthèse Excel et des CSV filtrés. Afficher le périmètre, les dates, la couverture et l’auteur de la génération. Télécharger depuis l’espace autorisé. |

La fiche utilisateur peut relier compte AD et Entra, postes, groupes et appartenances imbriquées, licences, boîte et archive, OneDrive, équipes, sites associés et applications identifiées. La navigation fonctionne aussi depuis une ressource collective vers les utilisateurs et leurs groupes. Un poste peut avoir plusieurs associations de nature différente et exister sans utilisateur principal.

Utiliser Fluent UI Blazor v5, une grille commune et un panneau de détail réutilisable. Prévoir des actions par lot avec aperçu du nombre d’objets affectés. Distinguer visuellement la source du projet, le tenant ou domaine interrogé, et la provenance d’une valeur. Les états sont compréhensibles par leur libellé et au clavier. Les libellés métier sont en français au départ, avec **Discovery** comme nom de fonctionnalité. Conserver les noms Microsoft et les valeurs d’origine lorsque leur traduction risquerait de changer le sens.

## 4. Matrice de collecte

**Ordre de préférence**. Pour Microsoft 365, utiliser Graph pour les données qu’il expose à la profondeur utile. Une section complémentaire indique ce que le script ou l’export ajoute, le périmètre qu’il couvre et le niveau de droits nécessaire. Quand les accès Graph sont insuffisants, Discovery affiche le manque et propose uniquement les alternatives disponibles pour cette section. Pour l’AD local, la collecte repose sur un script PowerShell exécuté depuis un poste autorisé qui accède au domaine.

| Domaine | Collecte directe dans Clarity | Approfondissement PowerShell ou import |
|---|---|---|
| **Entra ID et licences** | Graph pour utilisateurs, invités, groupes, propriétaires, appartenances, appareils, domaines, licences, SKU et métadonnées d’applications. | Scripts pour des périmètres ou rapports spécifiques. Qualification des dépendances SSO et des applications lorsque les métadonnées ne suffisent pas. |
| **AD DS** | Qualification de la forêt, des domaines et des OU dans le dossier. Rapprochement ultérieur avec Entra. | Module **ActiveDirectory**. Utilisateurs, ordinateurs, groupes, OU, SID, identifiants, attributs de synchronisation et appartenances. Couvrir les grands groupes, les groupes principaux et les références hors périmètre. |
| **Exchange Online** | Graph pour l’identité, les domaines et les rapports d’usage ou métadonnées accessibles qui contribuent à l’inventaire. | Module **ExchangeOnlineManagement**. Destinataires, boîtes utilisateur, partagées et ressources, archives, tailles, alias, délégations, groupes mail. Examiner au besoin les domaines acceptés, connecteurs et règles utiles au cadrage. |
| **SharePoint Online** | Graph pour sites, identifiants, groupes associés, ressources Teams liées et rapports disponibles. | **PnP.PowerShell** pour les détails de sites, bibliothèques, permissions et partages selon les capacités et les droits. Module **Microsoft.Online.SharePoint.PowerShell** ou profil de rapport administratif lorsque cette source correspond à la donnée attendue. |
| **OneDrive** | Graph pour l’association au compte, les métadonnées et rapports accessibles. | Exports administratifs ou script SharePoint/PnP pour compléter les sites personnels, volumes et contrôles de provisionnement. Signaler les identifiants masqués ou les liaisons impossibles. |
| **Teams** | Graph pour équipes, propriétaires, membres, canaux et résolution de sites associés quand possible. | Scripts permettant de qualifier les configurations ou dépendances non couvertes. Qualification du périmètre des chats et de la téléphonie dans le dossier. |
| **Intune et Autopilot** | Graph pour appareils, gestion, conformité, utilisateur associé, Autopilot, applications, profils, stratégies et affectations selon les endpoints pris en charge. | Scripts pour approfondir les sections justifiant un inventaire complémentaire. Pour chaque type, expliciter la couverture et la version d’API réellement testées. |
| **Microsoft 365 Apps et GPO** | Rapprochement des groupes, postes et politiques déjà collectés. | Scripts ciblés pour les paramètres Office, les sources de stratégie, GPO et populations concernées. |
| **Power BI** | Rattachement des identités et groupes déjà collectés. | Script ou profil d’import pour espaces de travail, rapports, modèles sémantiques, passerelles, sources et propriétaires. |
| **Azure, applications et infrastructure** | Graph pour les éléments d’identité disponibles. | Scripts Azure et locaux ou profils d’import pour abonnements, Log Analytics, serveurs, DNS, partages, imprimantes, services, certificats et dépendances applicatives. |

Le catalogue identifie chaque section par une clé stable, par exemple ad.groups, entra.users, exchange.mailboxStatistics ou sharepoint.sitePermissions. Pour chaque producteur, il précise les attributs et relations attendus, le périmètre supporté, la source de référence par attribut, les prérequis, les droits, les limites et la stratégie de rafraîchissement. Graph v1.0 est privilégié. Une API en préversion est signalée dans le catalogue et activable explicitement.

Le premier niveau couvre les objets, volumes et relations nécessaires au cadrage. Les analyses détaillées, comme les délégations ou permissions de site, se lancent sur un périmètre ciblé. Une dépendance partagée peut nécessiter de regarder au-delà de la population migrée. Collecter les métadonnées utiles au projet. Les contenus de messages et fichiers, mots de passe, jetons et clés de récupération sont hors du contrat.

## 5. Scripts, imports et contrat d’échange

### Téléchargement et exécution d’un script

1. Dans Discovery, le consultant choisit le dossier, l’environnement et les sections à approfondir. Clarity crée une demande de collecte, puis fournit le script versionné avec un fichier de paramètres de cette demande et la commande à lancer.
2. Le script vérifie la version de PowerShell, les modules, les droits de lecture et l’environnement réel. Le catalogue documente les OS, éditions PowerShell et versions de modules testés pour chaque script. Les prérequis manquants donnent une instruction explicite. L’installation des modules reste une action séparée du consultant.
3. Le consultant ouvre sa session locale avec le mode d’authentification pris en charge et autorisé par le client. Pour PnP en mode interactif, le script demande l’identifiant client d’une inscription Entra autorisée. Le consentement Graph de Clarity n’accorde aucun droit aux modules locaux. Les paramètres de collecte contiennent uniquement des métadonnées comme le tenant attendu, le site, l’OU ou la demande.
4. Le script indique sa progression par section, conserve les données valides issues d’une exécution partielle et dépose un ZIP local. Le consultant dépose ce fichier depuis son navigateur. Le poste d’exécution peut être distinct du poste qui importe le ZIP.
5. Clarity valide le paquet, rapproche la demande et l’environnement, et affiche un aperçu des objets, relations, erreurs et écarts attendus. Le consultant confirme son intégration. Les sections utiles deviennent consultables avec leur provenance et leur niveau de couverture.

Garder le script identique pour une version publiée et les paramètres dans un fichier séparé, afin de permettre une signature stable. Opter pour un ZIP de téléchargement unique par famille de collecte qui contient le script et les éventuels composants PowerShell communs requis. Distribuer leurs versions ensemble. Le script de collecte et le ZIP de résultats sont deux fichiers distincts dans le parcours. L’application traite le ZIP de résultats comme des données.

### Contrat canonique ZIP v1

Les scripts partagent un module PowerShell de génération du paquet. Tous publient les quatre fichiers UTF-8 ci-dessous à la racine. Les schémas JSON, les exemples et les tests du contrat sont versionnés dans le dépôt. Dates ISO 8601 UTC, identifiants externes sous forme de chaînes et volumes en octets lorsque la source fournit une mesure fiable.

| Fichier | Contenu minimal |
|---|---|
| **manifest.json** | schemaVersion, runId, requestId facultatif, producerId, producerVersion, environmentKind, environmentExternalId effectivement observé, scope demandé, startedAtUtc et completedAtUtc. Enregistrer les versions des modules utilisés et un identifiant de source lisible. |
| **objects.jsonl** | Une observation par ligne avec sectionId, type, namespace, externalId, observedAtUtc, attributes et clés externes alternatives disponibles. Une ligne peut apporter seulement les attributs couverts par son producteur. |
| **relationships.jsonl** | Une observation par ligne avec sectionId, relationType, from, to, observedAtUtc, propriétés éventuelles. Les extrémités portent type, namespace et externalId. Une référence peut rester à résoudre. |
| **results.json** | Une entrée par section avec sectionId, status, périmètre vérifié, champs ou types de relations couverts, compteurs, avertissements et erreurs nettoyées. Une section peut être complète, partielle, échouée ou non applicable. |

Un champ absent est une information non fournie. Une valeur explicitement vide est représentée selon le schéma du champ. Une section complète et vide peut déclarer zéro objet. Toujours vérifier le périmètre avant d’en déduire une absence. Un échec d’authentification ou une partie du périmètre inaccessible est représenté par un état échoué ou partiel. Chaque sectionId est unique dans le paquet et décrit une seule unité de périmètre comparable. Si une collecte traite deux unités avec des résultats différents, elle publie deux sections identifiables. Le catalogue associe chaque section à sa capacité de collecte.

L’environnement indiqué par le manifeste est une déclaration du producteur. L’utilisateur autorisé choisit le dossier dans Clarity. Le serveur vérifie que la demande éventuelle et l’environnement observé correspondent à cet espace et au dossier. Une discordance bloque la publication. Le serveur refuse les versions majeures inconnues, les paquets structurellement invalides et les références de section incohérentes. Un résultat partiel conforme peut être intégré.

### Autres imports

Les exports administratifs ou d’outillage transitent par **des profils d’import versionnés**, par exemple un rapport Microsoft, un export Quest ou un fichier spécifique déjà utilisé en mission. Un profil fixe le type de fichier accepté, les colonnes obligatoires, l’unité, l’identifiant de rapprochement et la couverture déclarée. L’aperçu montre les colonnes reconnues, les lignes rejetées et la source d’origine. L’adaptateur convertit les lignes valides en observations et relations canoniques. Les erreurs sur certaines lignes font apparaître la section comme partielle. Le consultant choisit un profil pris en charge et renseigne le périmètre lorsque le fichier ne le porte pas.

L’import d’une liste de population suit un assistant distinct. Il propose de sélectionner l’identifiant, résoudre les correspondances et appliquer les décisions par lot. La saisie structurée complète les constats manuels, avec auteur, date, environnement et référence à une preuve éventuelle.

### Chaîne commune

Graph alimente directement la chaîne de consolidation depuis un connecteur serveur. Le ZIP et les profils de fichiers passent par la validation et la normalisation, puis alimentent les mêmes traitements. Un mainteneur ajoute les scripts et profils au catalogue versionné du dépôt. Le consultant utilise les versions publiées dans Discovery. La provenance conserve le canal d’arrivée et la source réelle, par exemple script PowerShell utilisant ExchangeOnlineManagement ou rapport SharePoint importé.

### Exemple de consolidation

Graph inventorie un utilisateur Entra et son appartenance à un groupe. Le script Exchange remonte une boîte partagée, une taille d’archive et une délégation. Un script PnP ciblé ajoute l’accès d’un groupe au site d’une équipe. Clarity relie ces connaissances quand les identifiants ou règles de rapprochement le permettent, puis affiche l’origine de chaque lien. Si une partie des sites interrogés n’est pas accessible, le bilan SharePoint reste partiel et les liens précédemment observés restent consultables.

## 6. Modèle de données et règles de consolidation

| Entité | Champs et contraintes structurantes |
|---|---|
| **ClientWorkspace / WorkspaceMember** | Identité interne, nom et statut. Le membre est identifié par le tenant d’authentification et son identifiant stable Entra. Un rôle est attribué par espace. |
| **Project / ProjectEnvironment** | Client obligatoire. Scénario, état et paramètres de cadrage. L’environnement porte son rôle, son type, son identifiant externe et son périmètre. |
| **Connection / CollectionRequest** | Contexte client, environnement et type de collecteur. Périmètre, sections et référence de configuration. Conserver côté serveur les références aux moyens d’authentification Graph. Une demande PowerShell conserve le périmètre, la version du script et son état, sans identifiants de connexion locaux. |
| **CollectionRun / SectionRun / RawArtifact** | Dates, canal de collecte, producteur, versions, résultats et compteurs. Pour un fichier, conserver son empreinte et sa référence Blob dans le même périmètre. |
| **InventoryObject / ExternalKey** | Identité interne stable. Clé source composée de l’environnement, de l’espace de noms, du type et de l’identifiant externe. Conserver les identifiants alternatifs fournis par les sources. |
| **ObjectObservation / RelationshipObservation** | Objet ou relation, source, section, date, attributs et référence au run. L’état courant peut être projeté depuis ces observations pour les grilles. |
| **InventoryRelationship** | Type, extrémités résolues ou références en attente et méthode de résolution. Plusieurs sources peuvent étayer un même lien. |
| **Assessment / ScopeDecision / MappingDecision** | Auteur, date, objet ou population, valeur, justification et référence aux observations pertinentes. Les décisions sont historisées indépendamment des collectes. |
| **ExportJob / ExportArtifact / AuditEvent** | Périmètre ou filtres, génération, auteur, artefact et événements significatifs. Les références vers ces entités respectent la frontière client. |

Le premier schéma peut associer les observations à une collection JSON d’attributs et à une source commune. Indexer en colonnes les valeurs utilisées fréquemment en recherche, filtrage et rapprochement, comme les identifiants, UPN, SMTP, état et date. Les fichiers bruts restent dans Blob Storage. Les liens entre objets se gèrent dans Azure SQL. Le même modèle alimente la fiche utilisateur, les vues par workload et les vues de population.

### Identités et rapprochements

- **Un objet conserve son identité après un changement de nom**. Utiliser l’identifiant stable du système lorsque disponible. Garder l’UPN, l’adresse SMTP et l’URL comme clés secondaires historisées.
- **AD ↔ Entra**. Utiliser en priorité le lien de synchronisation vérifié et le sourceAnchor réellement configuré pour le dossier. Un UPN ou un SMTP exact et unique peut proposer un rapprochement soumis à revue si le lien fort manque.
- **Intune ↔ Entra ↔ AD**. Rapprocher à partir des identifiants exposés. Distinguer utilisateur principal Intune, propriétaire Entra et relation confirmée. Utiliser le nom du poste ou le numéro de série comme indices dans le bon périmètre, avec revue en cas d’ambiguïté.
- **Exchange et OneDrive ↔ utilisateur**. Préférer un identifiant de compte ou de destinataire lorsqu’il existe. En secours, proposer un rapprochement par UPN ou SMTP exact et unique. Conserver les boîtes partagées et de ressources comme objets indépendants.
- **Teams ↔ groupe M365 ↔ SharePoint**. Exploiter les identifiants et liens fournis par la collecte, puis conserver les sites de canaux comme objets distincts. Afficher un lien non résolu comme une relation à vérifier.
- **Source ↔ cible**. Les identifiants sont propres à leurs environnements. Proposer des correspondances à partir de règles documentées dans le dossier, comme un matricule unique ou l’UPN cible calculé. Conserver la justification et l’état proposé, validé, rejeté ou à revoir. Une cible prévue et une cible existante sont deux états distincts.
- **Groupes et accès**. Stocker les appartenances directes et dériver la transitivité avec gestion des cycles. Indiquer si un accès découle d’une appartenance, d’une délégation observée ou d’une qualification du consultant. Utiliser l’expression accès effectif uniquement si la collecte permet de l’établir.

### Provenance et choix de la valeur courante

Au clic sur la provenance, montrer le canal, le rapport, le connecteur ou le script et sa version, les modules lorsque disponibles, l’environnement, la date d’observation, le périmètre et le résultat de la section. Pour un import, afficher le nom du fichier, sa date de dépôt et le consultant qui l’a déposé. Un lien calculé affiche sa règle et les éléments qui le justifient. Une validation humaine affiche l’auteur et la justification. Un consultant peut corriger un rapprochement avec trace de la décision.

Pour un même attribut, choisir d’abord la source de référence définie pour le champ, puis l’observation complète la plus récente et comparable dans le périmètre concerné. Une valeur partielle peut enrichir une fiche si le champ est effectivement observé. L’écart entre deux sources comparables reste consultable. Une absence issue d’une collecte incomplète conserve la dernière valeur connue et rend la fraîcheur explicite. Une suppression exige un signal explicite ou une comparaison exhaustive et comparable. Réévaluer les décisions touchées lorsqu’un identifiant, un candidat ou une propriété critique change. Conserver les autres décisions.

### États et comptage

Une collecte Graph ou un job d’import peut être en attente, en cours, terminé, échoué ou annulé. Une demande PowerShell peut attendre un fichier, avoir un fichier à confirmer ou être intégrée. Chaque section publiée porte son propre état de couverture, complet, partiel, échoué ou non applicable. Une section complète peut retourner zéro objet. L’écran distingue **zéro**, **inconnu**, **hors périmètre** et **non applicable**. Un statut complet se rapporte toujours à un périmètre et à une liste de champs.

Dédupliquer les volumes par identifiant de ressource, par exemple un site affiché sous Teams et sous SharePoint. Quand un groupe de vingt utilisateurs et deux équipes utilisent la même ressource, une synthèse compte la ressource une fois et conserve les liens qui expliquent son inclusion. Un objet collectif est qualifié partagé si des dépendances connues couvrent les populations à migrer et à conserver. Si des liens restent incomplets, afficher la couverture de l’analyse.

## 7. Exécution des collectes et imports

- L’application exécute en arrière-plan les collectes Graph et les jobs d’import et d’export. Les demandes PowerShell se terminent côté consultant et reprennent dans Clarity au dépôt du ZIP. La dernière collecte valable reste visible pendant qu’une nouvelle demande attend son résultat.
- Pour Graph, limiter la concurrence par tenant et fournisseur, gérer pagination, limites de service et relances bornées. Réutiliser un curseur incrémental lorsque l’API et un périmètre identique le permettent. Refaire une collecte complète si le curseur ou le périmètre change.
- Enregistrer les jobs dans Azure SQL, avec état, tentative, réservation temporaire et progression utile. Le worker reprend ou relance les unités idempotentes après interruption. Publier chaque section conforme de manière transactionnelle.
- Valider le fichier puis présenter son aperçu avant publication des observations importées. Contrôler les autorisations au dépôt, à la confirmation et au traitement. Les erreurs structurelles bloquent l’intégration. Les sections valides d’un paquet bien formé peuvent être importées avec les informations d’échec des autres sections.
- Dédupliquer les fichiers par empreinte, dans le même dossier, environnement et périmètre. Une collision de runId avec un autre contenu est un conflit explicite. Éviter le double résultat si deux demandes arrivent en parallèle.
- Une relance Graph redémarre une section ciblée. Une nouvelle collecte PowerShell fournit un nouveau téléchargement ou réutilise la version validée pour le périmètre. Son ZIP est traité comme une observation ultérieure. Une annulation arrête les étapes en attente et garde les sections déjà publiées.
- Les exports utilisent des observations datées et une sélection déterminée au lancement. Enregistrer les filtres, l’auteur, les dates et la couverture. Le classeur contient la synthèse, l’inventaire utile et les exceptions. Les CSV servent aux listes et mappings. Les formats propres aux outils de migration sont ajoutés lorsque leur schéma a été vérifié.

## 8. Architecture et exploitation

**Stack retenue**. .NET 11, Blazor Web App Interactive Server, Fluent UI Blazor v5, EF Core 11 et Azure SQL Database. Hébergement Azure App Service, stockage des fichiers dans Azure Blob Storage, identité managée et Key Vault selon les accès. GitHub Actions construit et teste sur chaque push et déploie vers l’instance partagée à chaque push sur main, via OIDC.

| Composant | Responsabilité |
|---|---|
| **Clarity.Web** | UI, routes, authentification, autorisation et composition. Le contexte client d’une opération est dérivé de l’identité et du dossier autorisé. |
| **Clarity.Core** | Cas d’usage, entités, règles de couverture, consolidation, décisions et ports nécessaires. |
| **Clarity.Infrastructure** | EF Core, SQL, Blob, connecteurs Graph, validation ZIP, profils d’import, export et worker de jobs. Garder les modules métier dans le même déploiement. |
| **Clarity.Contracts** | Contrats versionnés et validation des modèles échangés, dès que l’import et les collecteurs sont intégrés. |
| **scripts/** | Scripts distribués et module PowerShell commun. Catalogue de capacités, prérequis, versions testées, paramètres et génération des paquets. Construire et publier les paquets de scripts avec l’application. |
| **infra/** et **.github/** | Bicep, workflow de compilation, tests, authentification OIDC, publication et migration de base explicite. Instructions durables pour Copilot. |

Le worker Graph et imports peut tourner dans le même App Service, avec des jobs persistants et une reprise après redémarrage. Les scripts sont livrés sous forme de téléchargements depuis Discovery et exécutés par les consultants. Créer un contexte EF par opération et scoper chaque job. Les grilles filtrent et paginent en base. Les logs et métriques renseignent durée, section, tentative, résultat et identifiant de corrélation, avec champs sensibles exclus. Exposer des contrôles de santé et suivre chaque échec jusqu’à son dossier dans l’interface autorisée.

**Démarrage local**. Documenter le SDK, la configuration de développement et la connexion SQL locale ou de développement. Le premier écran démarre en état vide. Les données de validation sont synthétiques et réservées aux tests. Les scripts, les dépendances et les paquets sont versionnés dans le dépôt.

**Déploiement Azure**. Bicep décrit App Service, Azure SQL, Blob Storage avec conteneurs privés, identité managée, configuration et supervision. GitHub Actions produit l’artefact, applique les migrations de façon explicite avec une identité autorisée, déploie le binaire et vérifie son démarrage. Sérialiser les déploiements d’un même environnement. Privilégier les migrations compatibles avec la version précédente. Prévoir un retour au dernier artefact applicatif et une stratégie documentée de traitement des changements de schéma. Définir les identifiants Azure, l’environnement GitHub et la région au provisionnement. Valider la disponibilité du runtime .NET choisi pour le plan et l’OS App Service. Le pipeline valide aussi les paquets PowerShell et leurs sorties synthétiques.

### Isolation, confidentialité et fichiers

- Exiger une authentification Entra pour les consultants. Appliquer les droits sur les entités et blobs avant chaque action. Scoper les requêtes, jobs, recherches, caches et exports au client. Utiliser des contraintes de référence tenant-aware et la sécurité au niveau des lignes comme défense supplémentaire sur les tables concernées. Revalider le contexte de sécurité pour les connexions SQL réutilisées.
- Gérer le consentement Graph par tenant et limiter les autorisations aux collectes activées. Séparer l’identité de déploiement, l’identité d’accès aux services Azure et les connexions Graph. Les scripts locaux s’authentifient dans leur contexte d’exécution et les paquets d’import ne contiennent aucun secret. Réserver les secrets serveur aux mécanismes de stockage prévus.
- Valider les fichiers avant publication. Limiter le ZIP en taille compressée et décompressée, en nombre d’entrées et en lignes. Lire les entrées attendues sans extraction arbitraire. Neutraliser les valeurs interprétables comme formules dans les exports tableurs. Les erreurs visibles expliquent la correction sans exposer les jetons ni les réponses brutes sensibles.
- Journaliser les accès sensibles, imports, modifications de périmètre et générations d’export. Paramétrer la durée de conservation par catégorie de fichier et de donnée avant le pilote réel. Permettre l’archivage et la suppression contrôlée d’un dossier, avec traitement cohérent du SQL et des blobs.

## 9. Ordre de réalisation pour l’agent

Ce séquençage définit l’ordre de construction du produit complet. Chaque lot se revoit et se valide indépendamment.

1. **Fondation**. Créer la solution, le shell Fluent, les pages vides, le modèle client/dossier/environnement, les contrôles d’autorisation, la première migration SQL, le README et la compilation CI. Fixer le déploiement Azure dans Bicep et le workflow, avec paramètres propres à l’environnement.
2. **Discovery et contrats**. Développer le catalogue, le GUID de demande, le module PowerShell commun, le ZIP v1, l’aperçu d’import, les jobs persistants et la couverture. Brancher Graph Entra et le script AD téléchargeable sur la même chaîne. Ajouter des paquets synthétiques au moment des tests d’import.
3. **Consolidation**. Développer l’identité stable, les observations, les liens, la provenance, la recherche, les fiches et la revue des correspondances. Relier AD, Entra et les appareils avant d’élargir les dépendances.
4. **Familles de collecte**. Déployer les sections Graph et les scripts complémentaires par famille. Livrer ExchangeOnlineManagement et PnP.PowerShell ou le module SharePoint adapté, puis les profils de rapports requis. Chaque section apporte ses permissions, sa matrice de versions, son contrôle des prérequis et sa recette de couverture. Étendre la même approche à Power BI, Azure, Office et à l’infrastructure.
5. **Cadrage de migration**. Ajouter les populations, les ressources partagées, le suivi des écarts, les comparaisons source/cible et les exports. Mettre en œuvre l’historique des décisions et leurs conditions de réexamen.
6. **Pilote**. Valider le parcours avec un cas représentatif anonymisé, vérifier l’isolation réelle, le redémarrage et les temps d’usage. Prioriser ensuite les enrichissements en fonction des opérations qui prennent encore du temps aux consultants.

### Recette de référence

| Cas | Résultat attendu |
|---|---|
| Dossier source seul | Collecte, exploration et synthèse utilisables. La comparaison source/cible s’active quand la cible est renseignée et les données utiles sont présentes. |
| Import identique en parallèle | Une seule intégration. Les statuts et fichiers restent associés au bon dossier. |
| Collecte Graph et import complémentaires | Une même ressource est visible avec les deux provenances. Les attributs et relations propres au script enrichissent cette ressource. |
| Script exécuté sur un poste séparé | Le consultant télécharge la collecte, lance le script dans un contexte autorisé et importe le ZIP depuis un autre poste. |
| Environnement incorrect | La vérification détecte un tenant ou domaine différent de celui du dossier et bloque la publication. |
| Import partiel et relance | Les sections réussies sont disponibles et les dernières observations utiles restent visibles. La section en erreur peut être relancée via son mode de collecte. |
| Rapport administratif pris en charge | Le profil produit les mêmes observations canoniques que les autres sources, avec les unités, le périmètre et les lignes rejetées visibles. |
| Redémarrage pendant une collecte | Reprise ou relance sans doublon et état du job compréhensible pour le consultant. |
| Tentative d’accès depuis un autre client | Refus côté serveur des lectures, téléchargements et mutations. Tester aussi un identifiant appartenant à un autre client. |
| Changement d’UPN et groupe imbriqué | L’objet reste stable et les liens restent explicables. Une correspondance ambiguë est soumise à revue. |
| Équipe et site partagés | Un seul site compté dans les volumes, avec les utilisateurs des deux populations et l’origine des liens. |
| Actualisation ciblée | Les faits changés sont visibles. Les décisions valides subsistent et celles affectées restent identifiables. |
| Source partielle ou rapport masqué | L’écran et l’export indiquent le manque de couverture. Aucun objet absent n’est déduit de ces seules données. |
| Export | Le fichier reflète les filtres, le périmètre et le millésime sélectionnés, avec compteurs réconciliables et provenance consultable. |

### Consignes à conserver dans le dépôt

Utiliser cette spécification comme référence produit. Consigner les choix locaux dans le README ou les décisions d’architecture. Garder les PR centrées sur un parcours vérifiable. Pour chaque connecteur, script ou profil, documenter les données attendues, les endpoints ou cmdlets effectivement utilisés, les droits, les versions testées et les limites de couverture. Implémenter les autorisations, l’idempotence et la provenance au même moment que les fonctionnalités qu’elles protègent. Publier la commande de validation et le résultat obtenu dans chaque PR.

## Références techniques

- [Politique de support .NET](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Fluent UI Blazor](https://github.com/microsoft/fluentui-blazor)
- [Microsoft Graph · Autorisations](https://learn.microsoft.com/en-us/graph/permissions-overview)
- [Microsoft Graph · Requêtes delta](https://learn.microsoft.com/en-us/graph/delta-query-overview)
- [Azure SQL · Architecture multi-tenant](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/service/sql-database)
- [Azure App Service · Déploiement via GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions?tabs=openid)
- [Exchange Online PowerShell · Module et prérequis](https://learn.microsoft.com/en-us/powershell/exchange/exchange-online-powershell-v2)
- [PnP PowerShell · Connexion et inscription Entra](https://pnp.github.io/powershell/cmdlets/Connect-PnPOnline)
- [PowerShell · Signature des scripts](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_signing)
