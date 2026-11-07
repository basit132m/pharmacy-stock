namespace PharmacyStock.Providers;

/// <summary>
/// Sample medicine list used by the demo data source, so the app can be seen working
/// before the SeaSoft database is connected. Real brands from the Pakistani market,
/// so the screens look like your actual shelf.
///
/// Columns: Name | Generic | Company | PackUnit | UnitsPerPack | RetailPrice | Supplier
/// </summary>
internal static class DemoCatalogue
{
    public const string Raw = """
    Velosef 500mg Capsule|Cephradine|GSK|Pack|12|420|Muller & Phipps
    Velosef 250mg Capsule|Cephradine|GSK|Pack|12|260|Muller & Phipps
    Velosef 125mg Syrup 60ml|Cephradine|GSK|Bottle|1|195|Muller & Phipps
    Augmentin 625mg Tablet|Amoxicillin + Clavulanic Acid|GSK|Pack|6|680|Muller & Phipps
    Augmentin 375mg Tablet|Amoxicillin + Clavulanic Acid|GSK|Pack|6|430|Muller & Phipps
    Augmentin 156mg Syrup 60ml|Amoxicillin + Clavulanic Acid|GSK|Bottle|1|330|Muller & Phipps
    Amoxil 500mg Capsule|Amoxicillin|GSK|Pack|12|290|Muller & Phipps
    Amoxil 250mg Syrup|Amoxicillin|GSK|Bottle|1|175|Muller & Phipps
    Ciproxin 500mg Tablet|Ciprofloxacin|Bayer|Pack|10|520|Premier Agency
    Ciproxin 250mg Tablet|Ciprofloxacin|Bayer|Pack|10|310|Premier Agency
    Flagyl 400mg Tablet|Metronidazole|Sanofi|Pack|20|180|United Distributors
    Flagyl 200mg Suspension|Metronidazole|Sanofi|Bottle|1|125|United Distributors
    Klaricid 500mg Tablet|Clarithromycin|Abbott|Pack|10|980|Muller & Phipps
    Klaricid 250mg Tablet|Clarithromycin|Abbott|Pack|10|620|Muller & Phipps
    Zithromax 500mg Tablet|Azithromycin|Pfizer|Pack|3|540|Premier Agency
    Azomax 500mg Tablet|Azithromycin|Getz Pharma|Pack|3|360|Dynamic Distributors
    Azomax 250mg Capsule|Azithromycin|Getz Pharma|Pack|6|300|Dynamic Distributors
    Ceftum 500mg Tablet|Cefuroxime|GSK|Pack|10|1150|Muller & Phipps
    Rocephin 1g Injection|Ceftriaxone|Roche|Vial|1|780|Premier Agency
    Oxidil 1g Injection|Ceftriaxone|Sami|Vial|1|280|Shaheen Distributors
    Maxipime 1g Injection|Cefepime|Bristol|Vial|1|1450|Premier Agency
    Tarivid 200mg Tablet|Ofloxacin|Sanofi|Pack|10|380|United Distributors
    Levoflox 500mg Tablet|Levofloxacin|Getz Pharma|Pack|10|540|Dynamic Distributors
    Doxycycline 100mg Capsule|Doxycycline|Atco|Pack|10|135|A.J. Distributors
    Septran DS Tablet|Co-trimoxazole|GSK|Pack|10|165|Muller & Phipps
    Panadol 500mg Tablet|Paracetamol|GSK|Pack|10|35|Muller & Phipps
    Panadol Extra Tablet|Paracetamol + Caffeine|GSK|Pack|10|55|Muller & Phipps
    Panadol CF Tablet|Paracetamol + Pseudoephedrine|GSK|Pack|10|70|Muller & Phipps
    Calpol 120mg Syrup 60ml|Paracetamol|GSK|Bottle|1|135|Muller & Phipps
    Brufen 400mg Tablet|Ibuprofen|Abbott|Pack|20|190|Muller & Phipps
    Brufen 600mg Tablet|Ibuprofen|Abbott|Pack|20|260|Muller & Phipps
    Brufen 100mg Syrup|Ibuprofen|Abbott|Bottle|1|165|Muller & Phipps
    Ponstan 500mg Tablet|Mefenamic Acid|Pfizer|Pack|20|240|Premier Agency
    Ponstan Forte Syrup|Mefenamic Acid|Pfizer|Bottle|1|195|Premier Agency
    Voltral 50mg Tablet|Diclofenac Sodium|Novartis|Pack|20|210|United Distributors
    Voltral Emulgel 30g|Diclofenac|Novartis|Tube|1|385|United Distributors
    Nims 100mg Tablet|Nimesulide|Hilton|Pack|20|150|Shaheen Distributors
    Disprin Tablet|Aspirin|Reckitt|Pack|10|30|Dynamic Distributors
    Loprin 75mg Tablet|Aspirin|Highnoon|Pack|30|95|A.J. Distributors
    Tramal 50mg Capsule|Tramadol|Searle|Pack|10|320|Shaheen Distributors
    Nuberol Forte Tablet|Paracetamol + Orphenadrine|Searle|Pack|20|280|Shaheen Distributors
    Buscopan 10mg Tablet|Hyoscine Butylbromide|Sanofi|Pack|20|260|United Distributors
    Buscopan Injection|Hyoscine Butylbromide|Sanofi|Pack|3|310|United Distributors
    Risek 20mg Capsule|Omeprazole|Getz Pharma|Pack|14|420|Dynamic Distributors
    Risek 40mg Capsule|Omeprazole|Getz Pharma|Pack|14|690|Dynamic Distributors
    Risek Insta Sachet|Omeprazole|Getz Pharma|Pack|10|480|Dynamic Distributors
    Nexum 40mg Capsule|Esomeprazole|Getz Pharma|Pack|14|790|Dynamic Distributors
    Zantac 150mg Tablet|Ranitidine|GSK|Pack|20|220|Muller & Phipps
    Motilium 10mg Tablet|Domperidone|Searle|Pack|30|195|Shaheen Distributors
    Motilium Suspension|Domperidone|Searle|Bottle|1|165|Shaheen Distributors
    Gaviscon Suspension 200ml|Sodium Alginate|Reckitt|Bottle|1|395|Dynamic Distributors
    Entamizole Tablet|Metronidazole + Diloxanide|Abbott|Pack|20|175|Muller & Phipps
    Imodium Capsule|Loperamide|Searle|Pack|10|120|Shaheen Distributors
    Duphalac Syrup 120ml|Lactulose|Abbott|Bottle|1|680|Muller & Phipps
    ORS Sachet Orange|Oral Rehydration Salt|Searle|Pack|1|25|Shaheen Distributors
    Glucophage 500mg Tablet|Metformin|Merck|Pack|20|135|United Distributors
    Glucophage 850mg Tablet|Metformin|Merck|Pack|20|195|United Distributors
    Glucophage XR 500mg|Metformin|Merck|Pack|20|260|United Distributors
    Amaryl 1mg Tablet|Glimepiride|Sanofi|Pack|10|185|United Distributors
    Amaryl 2mg Tablet|Glimepiride|Sanofi|Pack|10|280|United Distributors
    Amaryl 3mg Tablet|Glimepiride|Sanofi|Pack|10|375|United Distributors
    Diamicron MR 60mg Tablet|Gliclazide|Servier|Pack|15|540|Premier Agency
    Januvia 50mg Tablet|Sitagliptin|MSD|Pack|14|1250|Premier Agency
    Galvus Met 50/500 Tablet|Vildagliptin + Metformin|Novartis|Pack|14|980|United Distributors
    Lantus SoloStar Pen|Insulin Glargine|Sanofi|Pen|1|2450|United Distributors
    Mixtard 30/70 Vial|Human Insulin|Novo Nordisk|Vial|1|780|Premier Agency
    Humalog Mix25 Pen|Insulin Lispro|Lilly|Pen|1|2100|Premier Agency
    Zestril 5mg Tablet|Lisinopril|Atco|Pack|20|175|A.J. Distributors
    Zestril 10mg Tablet|Lisinopril|Atco|Pack|20|240|A.J. Distributors
    Concor 5mg Tablet|Bisoprolol|Merck|Pack|14|395|United Distributors
    Tenormin 50mg Tablet|Atenolol|AstraZeneca|Pack|14|165|Premier Agency
    Norvasc 5mg Tablet|Amlodipine|Pfizer|Pack|14|285|Premier Agency
    Amlopin 5mg Tablet|Amlodipine|Hilton|Pack|20|130|Shaheen Distributors
    Lipitor 20mg Tablet|Atorvastatin|Pfizer|Pack|14|690|Premier Agency
    Lipiget 20mg Tablet|Atorvastatin|Getz Pharma|Pack|14|340|Dynamic Distributors
    Lasix 40mg Tablet|Furosemide|Sanofi|Pack|20|145|United Distributors
    Inderal 10mg Tablet|Propranolol|Searle|Pack|50|120|Shaheen Distributors
    Plavix 75mg Tablet|Clopidogrel|Sanofi|Pack|14|1180|United Distributors
    Angised Tablet|Glyceryl Trinitrate|Pfizer|Pack|30|175|Premier Agency
    Cardura 2mg Tablet|Doxazosin|Pfizer|Pack|14|420|Premier Agency
    Betaloc 50mg Tablet|Metoprolol|AstraZeneca|Pack|20|195|Premier Agency
    Ventolin Inhaler 100mcg|Salbutamol|GSK|Inhaler|1|480|Muller & Phipps
    Ventolin Syrup 60ml|Salbutamol|GSK|Bottle|1|145|Muller & Phipps
    Ventolin Respule 2.5mg|Salbutamol|GSK|Pack|20|390|Muller & Phipps
    Seretide 250 Inhaler|Salmeterol + Fluticasone|GSK|Inhaler|1|2150|Muller & Phipps
    Flixotide Inhaler 125mcg|Fluticasone|GSK|Inhaler|1|1280|Muller & Phipps
    Montika 10mg Tablet|Montelukast|Getz Pharma|Pack|14|360|Dynamic Distributors
    Singulair 10mg Tablet|Montelukast|MSD|Pack|14|980|Premier Agency
    Hydryllin Syrup 120ml|Diphenhydramine|Abbott|Bottle|1|185|Muller & Phipps
    Actifed Syrup 60ml|Triprolidine|GSK|Bottle|1|210|Muller & Phipps
    Zyrtec 10mg Tablet|Cetirizine|GSK|Pack|10|195|Muller & Phipps
    Softin 10mg Tablet|Cetirizine|Searle|Pack|10|85|Shaheen Distributors
    Rigix 10mg Tablet|Cetirizine|Hilton|Pack|10|95|Shaheen Distributors
    Telfast 120mg Tablet|Fexofenadine|Sanofi|Pack|10|420|United Distributors
    Avil 25mg Tablet|Pheniramine|Sanofi|Pack|20|75|United Distributors
    Betnovate Cream 15g|Betamethasone|GSK|Tube|1|165|Muller & Phipps
    Betnovate-N Cream 15g|Betamethasone + Neomycin|GSK|Tube|1|195|Muller & Phipps
    Fucidin Cream 15g|Fusidic Acid|Leo|Tube|1|420|Premier Agency
    Polyfax Ointment 20g|Polymyxin + Bacitracin|GSK|Tube|1|195|Muller & Phipps
    Dermovate Cream 15g|Clobetasol|GSK|Tube|1|280|Muller & Phipps
    Canesten Cream 20g|Clotrimazole|Bayer|Tube|1|240|Premier Agency
    Daktarin Cream 15g|Miconazole|Searle|Tube|1|210|Shaheen Distributors
    Surbex Z Tablet|Multivitamin + Zinc|Abbott|Pack|30|480|Muller & Phipps
    Neurobion Tablet|Vitamin B Complex|Merck|Pack|30|340|United Distributors
    Neurobion Injection|Vitamin B Complex|Merck|Pack|3|390|United Distributors
    Calcit D3 Tablet|Calcium + Vitamin D3|Hilton|Pack|30|420|Shaheen Distributors
    Qalsan D Tablet|Calcium Carbonate|Searle|Pack|30|290|Shaheen Distributors
    Sangobion Capsule|Iron + Folic Acid|Merck|Pack|30|390|United Distributors
    Folic Acid 5mg Tablet|Folic Acid|Abbott|Pack|20|65|Muller & Phipps
    Indrop D 200000 IU Injection|Cholecalciferol|Hilton|Pack|1|145|Shaheen Distributors
    Osnate D Tablet|Calcium + Vitamin D3|Highnoon|Pack|30|450|A.J. Distributors
    Centrum Tablet|Multivitamin|Pfizer|Pack|30|1450|Premier Agency
    Nutrifactor Vitamin C 1000mg|Ascorbic Acid|Nutrifactor|Pack|20|390|A.J. Distributors
    Tegral 200mg Tablet|Carbamazepine|Novartis|Pack|30|260|United Distributors
    Rivotril 0.5mg Tablet|Clonazepam|Roche|Pack|30|185|Premier Agency
    Lexotanil 1.5mg Tablet|Bromazepam|Roche|Pack|30|240|Premier Agency
    Xanax 0.5mg Tablet|Alprazolam|Pfizer|Pack|30|320|Premier Agency
    Epival 500mg Tablet|Divalproex Sodium|Abbott|Pack|20|680|Muller & Phipps
    Lustral 50mg Tablet|Sertraline|Pfizer|Pack|14|620|Premier Agency
    Deanxit Tablet|Flupentixol + Melitracen|Lundbeck|Pack|30|480|Premier Agency
    Stilnox 10mg Tablet|Zolpidem|Sanofi|Pack|14|540|United Distributors
    Thyroxine 50mcg Tablet|Levothyroxine|GSK|Pack|100|280|Muller & Phipps
    Thyronorm 100mcg Tablet|Levothyroxine|Abbott|Pack|100|420|Muller & Phipps
    Duphaston 10mg Tablet|Dydrogesterone|Abbott|Pack|20|980|Muller & Phipps
    Primolut N Tablet|Norethisterone|Bayer|Pack|30|420|Premier Agency
    Evion 400mg Capsule|Vitamin E|Merck|Pack|10|165|United Distributors
    Dettol Antiseptic 250ml|Chloroxylenol|Reckitt|Bottle|1|395|Dynamic Distributors
    Pyodine Solution 60ml|Povidone Iodine|Brookes|Bottle|1|185|A.J. Distributors
    Strepsils Lozenges|Amylmetacresol|Reckitt|Pack|8|120|Dynamic Distributors
    Vicks VapoRub 25g|Menthol + Camphor|P&G|Jar|1|210|Dynamic Distributors
    Insulin Syringe 100 IU|Disposable Syringe|BD|Piece|1|35|A.J. Distributors
    Accu-Chek Active Strips|Glucose Test Strip|Roche|Pack|25|1850|Premier Agency
    Digital Thermometer|Thermometer|Omron|Piece|1|850|A.J. Distributors
    Surgical Face Mask|Face Mask|Generic|Pack|50|180|A.J. Distributors
    Cotton Roll 100g|Absorbent Cotton|Generic|Roll|1|145|A.J. Distributors
    Crepe Bandage 4 inch|Bandage|Generic|Piece|1|85|A.J. Distributors
    Leukoplast Tape 1 inch|Adhesive Tape|BSN|Piece|1|120|A.J. Distributors
    Disposable Syringe 5ml|Syringe|Becton|Piece|1|22|A.J. Distributors
    Glucerna SR Powder 400g|Diabetic Nutrition|Abbott|Tin|1|2250|Muller & Phipps
    Ensure Powder 400g|Adult Nutrition|Abbott|Tin|1|1980|Muller & Phipps
    Pediasure Vanilla 400g|Child Nutrition|Abbott|Tin|1|2100|Muller & Phipps
    Nan 1 Infant Formula 400g|Infant Formula|Nestle|Tin|1|1650|Dynamic Distributors
    Lactogen 1 Powder 400g|Infant Formula|Nestle|Tin|1|1280|Dynamic Distributors
    """;
}
